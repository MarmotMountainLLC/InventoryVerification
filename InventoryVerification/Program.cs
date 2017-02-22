using ScrapySharp.Html;
using ScrapySharp.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InventoryVerification
{
    public class Program
    {
        private static string SEARCH_URL = "marmot.com/search?q=";
        private enum SiteEnvironment { production, staging, development };
        public enum InventoryStatus { Good, MissingImages, NotPresent, Error };

        public static void Main(string[] args)
        {
            try
            {
                var fileName = GetInventoryFileName(args);
                var models = GetInventoryModelsFromFile(fileName);
                var environment = GetRunningEnvironment(args);
                var inventoryFails = VerifyInventory(models, environment);
                OutputInventoryFails(fileName, inventoryFails, environment);
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            Console.ReadKey();
        }

        static string GetInventoryFileName(string[] args)
        {
            if (args.Length > 0 && args[0].EndsWith("csv", StringComparison.InvariantCultureIgnoreCase))
                return args[0];

            Console.WriteLine("Please enter the inventory file name:");
            var filePath = Console.ReadLine();
            return filePath;
        }

        static IEnumerable<InventoryModel> GetInventoryModelsFromFile(string fileName)
        {
            Console.WriteLine("Parsing file: " + fileName);
            var rows = System.IO.File.ReadAllLines(fileName);
            return rows.Select(r => InventoryModel.CreateModel(r));
        }

        static SiteEnvironment GetRunningEnvironment(string[] args)
        {
            if (args == null)
                return SiteEnvironment.production;

            var envNames = Enum.GetNames(typeof(SiteEnvironment));
            foreach(var arg in args)
            {
                if (envNames.Any(env => env.Equals(arg, StringComparison.InvariantCultureIgnoreCase)))
                    return (SiteEnvironment)Enum.Parse(typeof(SiteEnvironment), arg, true);
            }

            return SiteEnvironment.production;
        }

        static IEnumerable<InventoryModel> VerifyInventory(IEnumerable<InventoryModel> inventory, SiteEnvironment environment)
        {
            Console.WriteLine($"Beginning inventory verification of {inventory.Count()} items against {environment}");
            var inventoryFails = new List<InventoryModel>();
            var scraper = new ScrapingBrowser();
            scraper.AllowAutoRedirect = true;
            scraper.AllowMetaRedirect = true;

            for (var i = 0; i < inventory.Count(); i++)
            {
                var item = inventory.ElementAt(i);
                Console.SetCursorPosition(0, Console.CursorTop);
                Console.Write($"Scanning #{i}");
                item.Status = VerifyUPC(scraper, item.InternationalArticleNumber, environment);
                if (item.Status != InventoryStatus.Good)
                    inventoryFails.Add(item);
            }

            return inventoryFails;
        }

        static InventoryStatus VerifyUPC(ScrapingBrowser scraper, string upc, SiteEnvironment environment)
        {
            try
            {
                // Nab the html by invoking the search pipe passing the UPC
                var result = environment == SiteEnvironment.production ? 
                    scraper.NavigateToPage(new Uri($"http://{SEARCH_URL}{upc}")) :
                    scraper.NavigateToPage(new Uri($"http://{environment}.{SEARCH_URL}{upc}"));
                // Find the primary image, if it exists
                var images = result.Find("img", By.Class("primary-image"));

                // Check for image presence
                if (images == null || images.Count() == 0)
                {
                    return InventoryStatus.NotPresent;
                }

                // Look for missing images
                if (images.Any(img => img.Attributes["data-lazy"].Value.Contains("noimage")))
                {
                    return InventoryStatus.MissingImages;
                }
                // TODO: Check for image 404s...?
                return result.Find("p", By.Class("not-available-msg")).Count() == 0 ? InventoryStatus.Good : InventoryStatus.NotPresent;
            }
            catch(Exception ex) {
                return InventoryStatus.Error;
            }
        }

        static void OutputInventoryFails(string fileName, IEnumerable<InventoryModel> inventoryFails, SiteEnvironment environment)
        {
            System.IO.File.WriteAllLines($"{environment}_inv_fails_" + fileName, inventoryFails.Select(inv => inv.ToString()));
        }
    }
}
