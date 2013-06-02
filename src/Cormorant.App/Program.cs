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

            var region = new Region();

            var region2 = new Region().GetAll().Cast<Region>();
            
            foreach (var region1 in region2)
            {
                Console.WriteLine(region1.Id);
            }
        }
    }
}
