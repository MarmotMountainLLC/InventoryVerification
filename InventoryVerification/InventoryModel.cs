using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InventoryVerification
{
    public class InventoryModel
    {
        public string SalesDocument { get; set; }
        public string SalesDocumentItem { get; set; }
        public string MaterialNumber { get; set; }
        public string GridValue { get; set; }
        public string InternationalArticleNumber { get; set; }
        public string Quantity { get; set; }

        public Program.InventoryStatus Status { get; set; }

        public static InventoryModel CreateModel(string commaSeparated)
        {
            var modelValues = commaSeparated.Split(',');
            if (modelValues.Length < 6)
                throw new InvalidOperationException("Input file contains funky data");

            return new InventoryModel
            {
                SalesDocument = modelValues[0],
                SalesDocumentItem = modelValues[1],
                MaterialNumber = modelValues[2],
                GridValue = modelValues[3],
                InternationalArticleNumber = modelValues[4],
                Quantity = modelValues[5]
            };
        }

        public override string ToString()
        {
            return $"{SalesDocument},{SalesDocumentItem},{MaterialNumber},{GridValue},{InternationalArticleNumber},{Quantity},{Status}";
        }
    }
}
