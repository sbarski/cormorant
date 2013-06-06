using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cormorant.App.Model
{
    public class Shippers : IDatabaseModel
    {
        public static bool hasInit = false;

        public Shippers()
        {
            if (!hasInit)
            {
                this.MapsToTable(databaseName: "Shippers");
                this.MapsToField(() => Id, databasePropertyName: "ShipperID").IsPrimaryKey(PKGenerationStrategy.Identity);
                this.MapsToField(() => CompanyName, databasePropertyName: "CompanyName");
                this.MapsToField(() => Phone, databasePropertyName: "Phone");


                hasInit = true;
            }
        }

        public int Id { get; set; }
        public string CompanyName { get; set; }
        public string Phone { get; set; }

    }
}
