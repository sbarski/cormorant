using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Cormorant.App.Model;

namespace Cormorant.App
{
    class Program
    {
        static void Main(string[] args)
        {
            var db = new Database("(local)", string.Empty, string.Empty, "Northwind");

            Console.WriteLine(db.CanConnectToDatabase());

            var region2 = new Region();

            var region3 = region2.GetAll();
            
            foreach (var region1 in region3)
            {
                Console.WriteLine(region1.Id);
            }

            var updR = region3.FirstOrDefault();
                
            updR.RegionDescription = "Peter";

            updR.Update();
        }
    }
}
