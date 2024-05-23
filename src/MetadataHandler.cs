using System;
using System.Collections.Generic;
using System.Linq;
//using System.Data;
using System.Text;
using Landis.Library.Metadata;
using Landis.Core;
using Landis.Utilities;
using System.IO;
using Flel = Landis.Utilities;

namespace Landis.Extension.BaseHarvest
{
    public static class MetadataHandler
    {
        
        public static ExtensionMetadata Extension {get; set;}

        public static void InitializeMetadata(int Timestep, string MapFileName, /*string HarvestMapName, ICore mCore,*/ string eventLogName, string summaryLogName)
        {

            ScenarioReplicationMetadata scenRep = new ScenarioReplicationMetadata() {
                RasterOutCellArea = PlugIn.ModelCore.CellArea,
                TimeMin = PlugIn.ModelCore.StartTime,
                TimeMax = PlugIn.ModelCore.EndTime,
            };

            Extension = new ExtensionMetadata(PlugIn.ModelCore)
            //Extension = new ExtensionMetadata()
            {
                Name = PlugIn.ExtensionName,
                TimeInterval = Timestep, //change this to PlugIn.TimeStep for other extensions
                ScenarioReplicationMetadata = scenRep
            };

            //---------------------------------------
            //          table outputs:   
            //---------------------------------------

            CreateDirectory(eventLogName);
            CreateDirectory(summaryLogName);
            PlugIn.eventLog = new MetadataTable<EventsLog>(eventLogName);
            PlugIn.summaryLog = new MetadataTable<SummaryLog>(summaryLogName);
            //PlugIn.eventLog = new MetadataTable<EventsLog>("Harvest-event-log.csv");
            //PlugIn.summaryLog = new MetadataTable<SummaryLog>("Harvest-summary-log.csv");

            PlugIn.ModelCore.UI.WriteLine("   Generating event table...");
            OutputMetadata tblOut_events = new OutputMetadata()
            {
                Type = OutputType.Table,
                Name = "EventLog",
                FilePath = PlugIn.eventLog.FilePath,
                Visualize = false,
            };
            tblOut_events.RetriveFields(typeof(EventsLog));
            Extension.OutputMetadatas.Add(tblOut_events);

            PlugIn.ModelCore.UI.WriteLine("   Generating summary table...");
            OutputMetadata tblOut_summary = new OutputMetadata()
            {
                Type = OutputType.Table,
                Name = "SummaryLog",
                FilePath = PlugIn.summaryLog.FilePath,
                Visualize = true,
            };
            tblOut_summary.RetriveFields(typeof(SummaryLog));
            Extension.OutputMetadatas.Add(tblOut_summary);

            //---------------------------------------            
            //          map outputs:         
            //---------------------------------------

            //OutputMetadata mapOut_BiomassRemoved = new OutputMetadata()
            //{
            //    Type = OutputType.Map,
            //    Name = "biomass removed",
            //    FilePath = @HarvestMapName,
            //    Map_DataType = MapDataType.Continuous,
            //    Map_Unit = FieldUnits.Mg_ha,
            //    Visualize = true,
            //};
            //Extension.OutputMetadatas.Add(mapOut_BiomassRemoved);


            OutputMetadata mapOut_Prescription = new OutputMetadata()
            {
                Type = OutputType.Map,
                Name = "prescription",
                FilePath = @MapFileName,
                Map_DataType = MapDataType.Nominal,
                Visualize = true,
                //Map_Unit = "categorical",
            };
            Extension.OutputMetadatas.Add(mapOut_Prescription);

            //---------------------------------------
            MetadataProvider mp = new MetadataProvider(Extension);
            mp.WriteMetadataToXMLFile("Metadata", Extension.Name, Extension.Name);




        }
        public static void CreateDirectory(string path)
        {
            //Require.ArgumentNotNull(path);
            path = path.Trim(null);
            if (path.Length == 0)
                throw new ArgumentException("path is empty or just whitespace");

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Flel.Directory.EnsureExists(dir);
            }

            //return new StreamWriter(path);
            return;
        }
    }
}
