using Landis.Utilities;
using Landis.Core;
using Landis.Library.HarvestManagement;
using Landis.Library.Succession;
using Landis.Library.Metadata;
using Landis.SpatialModeling;
using System.Collections.Generic;
using System.IO;
using System;

namespace Landis.Extension.BaseHarvest
{
    public class PlugIn
        : HarvestExtensionMain
    {
        public static readonly string ExtensionName = "Base Harvest";

        private IManagementAreaDataset managementAreas;
        private PrescriptionMaps prescriptionMaps;
        public static MetadataTable<EventsLog> eventLog;
        public static MetadataTable<SummaryLog> summaryLog;
        private static int event_id;
        private static double current_rank;     //need a global to keep track of the current stand's rank.  just for log file.

        public static int[] totalSites;
        public static int[] totalDamagedSites;
        public static int[,] totalSpeciesCohorts;
        // 2015-09-14 LCB Track prescriptions as they are reported in summary log so we don't duplicate
        public static bool[] prescriptionReported;

        private IInputParameters parameters;
        private static ICore modelCore;


        //---------------------------------------------------------------------

        public PlugIn()
            : base(ExtensionName)
        {
        }

        //---------------------------------------------------------------------

        public static ICore ModelCore
        {
            get
            {
                return modelCore;
            }
        }
        //---------------------------------------------------------------------

        public override void LoadParameters(string dataFile, ICore mCore)
        {
            modelCore = mCore;
            Landis.Library.HarvestManagement.Main.InitializeLib(modelCore);

            HarvestExtensionMain.RepeatStandHarvestedEvent += RepeatStandHarvested;
            HarvestExtensionMain.RepeatPrescriptionFinishedEvent += RepeatPrescriptionHarvested;

            InputParametersParser parser = new InputParametersParser(mCore.Species);
            parameters = Landis.Data.Load<IInputParameters>(dataFile, parser);
            if (parser.RoundedRepeatIntervals.Count > 0)
            {
                PlugIn.ModelCore.UI.WriteLine("NOTE: The following repeat intervals were rounded up to");
                PlugIn.ModelCore.UI.WriteLine("      ensure they were multiples of the harvest timestep:");
                PlugIn.ModelCore.UI.WriteLine("      File: {0}", dataFile);
                foreach (RoundedInterval interval in parser.RoundedRepeatIntervals)
                    PlugIn.ModelCore.UI.WriteLine("      At line {0}, the interval {1} rounded up to {2}",
                                 interval.LineNumber,
                                 interval.Original,
                                 interval.Adjusted);
            }
            if (parser.ParserNotes.Count > 0)
            {
                foreach (List<string> nList in parser.ParserNotes)
                {
                    foreach (string nLine in nList)
                    {
                        PlugIn.ModelCore.UI.WriteLine(nLine);
                    }
                }
            }
        }
        //---------------------------------------------------------------------

        public override void Initialize()
        {
            //initialize event id
            event_id = 1;

            MetadataHandler.InitializeMetadata(parameters.Timestep, parameters.PrescriptionMapNames, parameters.EventLog, parameters.SummaryLog);
            Timestep = parameters.Timestep;
            managementAreas = parameters.ManagementAreas;
            PlugIn.ModelCore.UI.WriteLine("   Reading management-area map {0} ...", parameters.ManagementAreaMap);
            ManagementAreas.ReadMap(parameters.ManagementAreaMap, managementAreas);

            //readMap reads the stand map and adds all the stands to a management area
            PlugIn.ModelCore.UI.WriteLine("   Reading stand map {0} ...", parameters.StandMap);
            Stands.ReadMap(parameters.StandMap);

            //finish initializing SiteVars
            SiteVars.GetExternalVars();

            //finish each managementArea's initialization
            //after reading the stand map, finish the initializations
            foreach (ManagementArea mgmtArea in managementAreas)
                mgmtArea.FinishInitialization();

            prescriptionMaps = new PrescriptionMaps(parameters.PrescriptionMapNames);
        }

        //---------------------------------------------------------------------

        public override void Run()
        {
            SiteVars.GetExternalVars(); // ReInitialize();
            SiteVars.Prescription.ActiveSiteValues = null;
            SiteVars.CohortsDamaged.ActiveSiteValues = 0;


            //harvest each management area in the list
            foreach (ManagementArea mgmtArea in managementAreas) {
                totalSites = new int[Prescription.Count];
                totalDamagedSites = new int[Prescription.Count];
                totalSpeciesCohorts = new int[Prescription.Count, modelCore.Species.Count];
                prescriptionReported = new bool[Prescription.Count];

                mgmtArea.HarvestStands();
                //and record each stand that's been harvested

                foreach (Stand stand in mgmtArea) {
                    if (stand.Harvested)
                        WriteLogEntry(mgmtArea, stand);

                }

                // updating for preventing establishment
                foreach (Stand stand in mgmtArea) 
                {
                    if (stand.Harvested && stand.LastPrescription.PreventEstablishment) 
                    {

                        List<ActiveSite> sitesToDelete = new List<ActiveSite>();

                        foreach (ActiveSite site in stand) {
                            if (SiteVars.CohortsDamaged[site] > 0)
                            {
                                Reproduction.PreventEstablishment(site);
                                sitesToDelete.Add(site);
                            }

                        }

                        foreach (ActiveSite site in sitesToDelete) {
                            stand.DelistActiveSite(site);
                        }
                    }

                } // foreach (Stand stand in mgmtArea)

                foreach (AppliedPrescription aprescription in mgmtArea.Prescriptions)
                {
                    if (modelCore.CurrentTime <= aprescription.EndTime)
                        WriteSummaryLogEntry(mgmtArea, aprescription);
                }
            }
            prescriptionMaps.WriteMap(PlugIn.ModelCore.CurrentTime);


        }

        //---------------------------------------------------------------------

        // Event handler when a stand has been harvested in a repeat step
        public static void RepeatStandHarvested(object sender,
                                         RepeatHarvestStandHarvestedEvent.Args eventArgs)
        {
            WriteLogEntry(eventArgs.MgmtArea, eventArgs.Stand, eventArgs.RepeatNumber);
        }

        //---------------------------------------------------------------------

        // Event handler when a prescription has finished a repeat event
        public static void RepeatPrescriptionHarvested(object sender,
                                         RepeatHarvestPrescriptionFinishedEvent.Args eventArgs)
        {
            WriteSummaryLogEntry(eventArgs.MgmtArea, eventArgs.Prescription, eventArgs.RepeatNumber,
                eventArgs.LastHarvest);
        }

        //---------------------------------------------------------------------

        public static int EventId {
            get {
                return event_id;
            }

            set {
                event_id = value;
            }
        }

        //---------------------------------------------------------------------

        public static double CurrentRank {
            get {
                return current_rank;
            }

            set {
                current_rank = value;
            }
        }

        //---------------------------------------------------------------------
        public static void WriteLogEntry(ManagementArea mgmtArea, Stand stand, uint repeatNumber = 0)
        {
            int damagedSites = 0;
            int cohortsDamaged = 0;
            int standPrescriptionNumber = 0;

            foreach (ActiveSite site in stand) {
                //set the prescription name for this site
                if (SiteVars.Prescription[site] != null)
                {
                    standPrescriptionNumber = SiteVars.Prescription[site].Number;
                    SiteVars.PrescriptionName[site] = SiteVars.Prescription[site].Name;
                    SiteVars.TimeOfLastEvent[site] = PlugIn.ModelCore.CurrentTime;
                }
                int cohortsDamagedAtSite = SiteVars.CohortsDamaged[site];
                cohortsDamaged += cohortsDamagedAtSite;
                if (cohortsDamagedAtSite > 0) {
                    damagedSites++;
                }
            }


            totalSites[standPrescriptionNumber] += stand.SiteCount;
            totalDamagedSites[standPrescriptionNumber] += damagedSites;

            //csv string for log file, contains species kill count
            //string species_count = "";
            double[] species_count = new double[modelCore.Species.Count];

            foreach (ISpecies species in PlugIn.ModelCore.Species)
            {
                int cohortCount = stand.DamageTable[species];
                species_count[species.Index] += cohortCount;
                totalSpeciesCohorts[standPrescriptionNumber, species.Index] += cohortCount;
            }
            //Trim trailing comma so we don't add an extra column
            //species_count = species_count.TrimEnd(',');


            //now that the damage table for this stand has been recorded, clear it!!
            stand.ClearDamageTable();

            //write to log file:
            //current time
            //management area's map code
            //the prescription that caused this harvest
            //stand's map code
            //stand's age
            //stand's current rank
            //total sites in the stand
            //damaged sites from this stand
            //cohorts killed in this stand, by this harvest
            //and only record stands where a site has been damaged
            //log.WriteLine("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10}",
            //              PlugIn.ModelCore.CurrentTime, mgmtArea.MapCode, stand.PrescriptionName, stand.MapCode, stand.EventId,
            //              stand.Age, stand.HarvestedRank, stand.SiteCount, damagedSites, cohortsDamaged, species_count);

            string name = stand.PrescriptionName;

            if (repeatNumber != 0)
            {
                name = name + "(" + repeatNumber + ")";
            }

            eventLog.Clear();
            EventsLog el = new EventsLog();
            el.Time = modelCore.CurrentTime;
            el.ManagementArea = mgmtArea.MapCode;
            el.Prescription = name;
            el.Stand = stand.MapCode;
            el.EventID = stand.EventId;
            el.StandAge = stand.Age;
            el.StandRank = Convert.ToInt32(stand.HarvestedRank);
            el.NumberOfSites = stand.SiteCount;
            el.HarvestedSites = damagedSites;
            el.TotalCohortsHarvested = cohortsDamaged;
            el.CohortsHarvested_ = species_count;

            eventLog.AddObject(el);
            eventLog.WriteToFile();
        }

        public static void WriteSummaryLogEntry(ManagementArea mgmtArea, AppliedPrescription prescription, uint repeatNumber = 0, bool lastHarvest = false)
        {
            //string species_string = "";
            double[] species_count = new double[modelCore.Species.Count];
            foreach (ISpecies species in PlugIn.ModelCore.Species)
                species_count[species.Index] += totalSpeciesCohorts[prescription.Prescription.Number, species.Index];

            if (totalSites[prescription.Prescription.Number] > 0 && prescriptionReported[prescription.Prescription.Number] != true)
            {
                string name = prescription.Prescription.Name;

                if (repeatNumber > 0)
                {
                    name = name + "(" + repeatNumber + ")";
                }
                //summaryLog.WriteLine("{0},{1},{2},{3}{4}",
                //    PlugIn.ModelCore.CurrentTime,
                //    mgmtArea.MapCode,
                //    prescription.Name,
                //    totalDamagedSites[prescription.Number],
                //    species_string);
                summaryLog.Clear();
                SummaryLog sl = new SummaryLog();
                sl.Time = modelCore.CurrentTime;
                sl.ManagementArea = mgmtArea.MapCode;
                sl.Prescription = name;
                sl.HarvestedSites = totalDamagedSites[prescription.Prescription.Number];
                sl.CohortsHarvested_ = species_count;
                summaryLog.AddObject(sl);
                summaryLog.WriteToFile();

                // Do not mark this as recorded until the final summary is logged. Because repeat steps will be
                // recorded first and then new initiations, mark this as reported once the initiation step is complete
                if (repeatNumber == 0 || (ModelCore.CurrentTime > prescription.EndTime && lastHarvest))
                {
                    prescriptionReported[prescription.Prescription.Number] = true;
                }

                // Clear the log for the initial harvests
                if (lastHarvest)
                {
                    totalDamagedSites[prescription.Prescription.Number] = 0;

                    foreach (ISpecies species in modelCore.Species)
                    {
                        totalSpeciesCohorts[prescription.Prescription.Number, species.Index] = 0;
                    }
                }
            }
        }

        //---------------------------------------------------------------------
        public override void CleanUp()
        {
            Landis.Library.SiteHarvest.Main.ResetLib(ModelCore);
        }

        //---------------------------------------------------------------------
        //public void Mark(ManagementArea mgmtArea, Stand stand) {
        //} //

    }
}
