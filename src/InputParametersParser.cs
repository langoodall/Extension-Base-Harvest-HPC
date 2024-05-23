using Landis.Core;

namespace Landis.Extension.BaseHarvest
{
    /// <summary>
    /// A parser that reads harvest parameters from text input.
    /// </summary>
    public class InputParametersParser
        : Landis.Library.HarvestManagement.InputParametersParser
    {
        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="speciesDataset">
        /// The dataset of species to look up species' names in.
        /// </param>
        public InputParametersParser(ISpeciesDataset speciesDataset)
            : base(PlugIn.ExtensionName, speciesDataset)
        {
        }
    }
}
