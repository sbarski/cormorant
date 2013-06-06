using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cormorant.App.Model
{
    public class Region : IDatabaseModel
    {
        public static bool hasInit = false;

        public Region()
        {
            if (!hasInit)
            {
                this.MapsToTable(databaseName: "Region");
                this.MapsToField(() => Id, databasePropertyName: "RegionID").IsPrimaryKey(PKGenerationStrategy.Identity);
                this.MapsToField(() => RegionDescription, databasePropertyName: "RegionDescription");

                hasInit = true;
            }
        }

        public int Id { get; set; }
        public string RegionDescription { get; set; }
    }
}
