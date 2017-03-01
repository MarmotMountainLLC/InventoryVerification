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
        private static string DEV_SEARCH_URL = "http://development-web-jta.demandware.net/s/Marmot_US/search?q=";
         
        private enum SiteEnvironment { production, staging, development };
        private enum RunType { inventory, swatch, alt, all };
        public enum InventoryStatus { Good, MissingImages, NotPresent, Error };
        
        public static void Main(string[] args)
        {
            try
            {
                var fileName = GetInventoryFileName(args);
                var models = GetInventoryModelsFromFile(fileName);
                var environment = GetRunningEnvironment(args);
                var runType = GetRunType(args);

                switch(runType)
                {
                    case RunType.inventory:
                        RunInventoryCheck(models, fileName, environment);
                        break;
                    case RunType.swatch:
                        RunSwatchCheck(models, environment);
                        break;
                    case RunType.alt:
                        RunAltCheck(models, environment);
                        break;
                    default:
                        RunInventoryCheck(models, fileName, environment);
                        RunSwatchCheck(models, environment);
                        RunAltCheck(models, environment);
                        break;
                }
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            Console.ReadKey();
        }

        static void RunInventoryCheck(IEnumerable<InventoryModel> inventory, string fileName, SiteEnvironment environment)
        {
            var inventoryFails = Task.Run(() => VerifyInventory(inventory, environment)).Result;
            OutputInventoryFails(fileName, inventoryFails, environment);
        }

        static void RunAltCheck(IEnumerable<InventoryModel> inventory, SiteEnvironment environment)
        {
            var altFails = Task.Run(() => VerifyAlts(inventory, environment)).Result;
            OutputImageFails("alts", altFails, environment);
        }

        static void RunSwatchCheck(IEnumerable<InventoryModel> inventory, SiteEnvironment environment)
        {
            var swatchFails = Task.Run(() => VerifySwatches(inventory, environment)).Result;
            OutputImageFails("swatches", swatchFails, environment);
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

        static RunType GetRunType(string[] args)
        {
            if (args == null)
                return RunType.inventory;

            var runTypeNames = Enum.GetNames(typeof(RunType));
            foreach (var arg in args)
            {
                if (runTypeNames.Any(run => run.Equals(arg, StringComparison.InvariantCultureIgnoreCase)))
                    return (RunType)Enum.Parse(typeof(RunType), arg, true);
            }

            return RunType.inventory;
        }

        static async Task<IEnumerable<InventoryModel>> VerifyInventory(IEnumerable<InventoryModel> inventory, SiteEnvironment environment)
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
                item.Status = await VerifyUPC(scraper, item.InternationalArticleNumber, environment);
                if (item.Status != InventoryStatus.Good)
                    inventoryFails.Add(item);
            }

            return inventoryFails;
        }

        static async Task<InventoryStatus> VerifyUPC(ScrapingBrowser scraper, string upc, SiteEnvironment environment)
        {
            try
            {
                // Nab the html by invoking the search pipe passing the UPC
                var searchUrl = environment == SiteEnvironment.production ? 
                    $"http://{SEARCH_URL}{upc}" :
                    environment == SiteEnvironment.development ?
                    $"{DEV_SEARCH_URL}{upc}" :
                    $"http://{environment}.{SEARCH_URL}{upc}";
                var result = await scraper.NavigateToPageAsync(new Uri(searchUrl));
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
                
                return result.Find("p", By.Class("not-available-msg")).Count() == 0 ? InventoryStatus.Good : InventoryStatus.NotPresent;
            }
            catch {
                return InventoryStatus.Error;
            }
        }
        
        static async Task<IEnumerable<string>> VerifySwatches(IEnumerable<InventoryModel> inventory, SiteEnvironment environment)
        {
            var styles = inventory.Select(i => i.MaterialNumber.Split('-')[0]).Distinct();

            Console.WriteLine($"Beginning Swatch verification of {styles.Count()} items against {environment}");
            var swatchFails = new List<string>();
            var scraper = new ScrapingBrowser();
            scraper.AllowAutoRedirect = true;
            scraper.AllowMetaRedirect = true;

            for (var i = 0; i < styles.Count(); i++)
            {
                var style = styles.ElementAt(i);
                Console.SetCursorPosition(0, Console.CursorTop);
                Console.Write($"Scanning #{i}");
                if (! await VerifySwatchSet(scraper, style, environment))
                    swatchFails.Add(style);
            }

            return swatchFails;
        }

        static async Task<bool> VerifySwatchSet(ScrapingBrowser scraper, string style, SiteEnvironment environment)
        {
            try
            {
                var searchUrl = environment == SiteEnvironment.production ?
                    $"http://{SEARCH_URL}{style}" :
                    environment == SiteEnvironment.development ?
                    $"{DEV_SEARCH_URL}{style}" :
                    $"http://{environment}.{SEARCH_URL}{style}";
                var result = await scraper.NavigateToPageAsync(new Uri(searchUrl));
                var swatchAnchors = result.Find("a", By.Class("swatchanchor"));

                // Check for image presence
                if (swatchAnchors == null || swatchAnchors.Count() == 0)
                {
                    return false;
                }

                var swatchUrls = new List<string>();
                foreach (var anchor in swatchAnchors)
                {
                    var imgNode = anchor.ChildNodes.FirstOrDefault(n => n.NodeType == HtmlAgilityPack.HtmlNodeType.Element);
                    if(imgNode != null)
                        swatchUrls.Add(imgNode.Attributes["src"].Value);
                }

                foreach (var swatchUrl in swatchUrls)
                {
                    using (var webResponse = await System.Net.WebRequest.CreateHttp(swatchUrl).GetResponseAsync())
                    {
                        var statusCode = (int)((System.Net.HttpWebResponse)webResponse).StatusCode;
                        if (statusCode < 200 || statusCode >= 300)
                            return false;
                    }
                }
                return true;
            }
            catch
            {
                // Don't count it if it's not there
                return true;
            }
        }

        static async Task<IEnumerable<string>> VerifyAlts(IEnumerable<InventoryModel> inventory, SiteEnvironment environment)
        {
            var materials = new Dictionary<string, string>();
            // Only care about the material number, but need a UPC to search on
            foreach (var item in inventory)
                materials[item.MaterialNumber] = item.InternationalArticleNumber;

            Console.WriteLine($"Beginning Alt verification of {materials.Count()} items against {environment}");
            var materialFails = new List<string>();
            var scraper = new ScrapingBrowser();
            scraper.AllowAutoRedirect = true;
            scraper.AllowMetaRedirect = true;

            for (var i = 0; i < materials.Count(); i++)
            {
                var material = materials.ElementAt(i);
                Console.SetCursorPosition(0, Console.CursorTop);
                Console.Write($"Scanning #{i}");
                if (!await VerifyAltSet(scraper, material.Value, environment))
                    materialFails.Add(material.Key);
            }

            return materialFails;
        }

        static async Task<bool> VerifyAltSet(ScrapingBrowser scraper, string material, SiteEnvironment environment)
        {
            try
            {
                var searchUrl = environment == SiteEnvironment.production ?
                    $"http://{SEARCH_URL}{material}" :
                    environment == SiteEnvironment.development ?
                    $"{DEV_SEARCH_URL}{material}" :
                    $"http://{environment}.{SEARCH_URL}{material}";
                var result = await scraper.NavigateToPageAsync(new Uri(searchUrl));
                var thumbs = result.Find("img", By.Class("productthumbnail"));

                // Check for image presence
                if (thumbs == null || thumbs.Count() == 0)
                {
                    return false;
                }

                var altUrls = thumbs.Select(thumb => thumb.Attributes["src"].Value);
                foreach (var altUrl in altUrls)
                {
                    var webResponse = await System.Net.WebRequest.CreateHttp(altUrl).GetResponseAsync();
                    var statusCode = (int)((System.Net.HttpWebResponse)webResponse).StatusCode;
                    if (statusCode < 200 || statusCode >= 300)
                        return false;
                }
                return true;
            }
            catch
            {
                return true;
            }
        }

        static void OutputInventoryFails(string fileName, IEnumerable<InventoryModel> inventoryFails, SiteEnvironment environment)
        {
            System.IO.File.WriteAllLines($"{environment}_inv_fails_" + fileName, inventoryFails.Select(inv => inv.ToString()));
        }

        static void OutputImageFails(string prefix, IEnumerable<string> imageFails, SiteEnvironment environment)
        {
            System.IO.File.WriteAllLines($"{environment}_{prefix}_fails.csv", imageFails);
        }
    }
}
