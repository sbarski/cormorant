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

            var shippers = new Shippers().GetAll();

            foreach (var shipperse in shippers)
            {
                Console.WriteLine(shipperse.Phone);
            }

            var ship1 = shippers.FirstOrDefault();

            ship1.CompanyName = "Peter";

            ship1.Update("Transact");

            var ship = new Shippers() {CompanyName = "Appfail", Phone = "2323242"};

            ship.Insert("Transact Shipper");

            var peter = new Peter() {Comment = "Blah"};

            peter.Insert("blah");

        }
    }
}
