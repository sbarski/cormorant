using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cormorant.App.Model
{
    public class Peter : IDatabaseModel
    {
        public static bool hasInit = false;

        public Peter()
        {
            if (!hasInit)
            {
                this.MapsToTable(databaseName: "Peter");
                this.MapsToField(() => Id, databasePropertyName: "PeterId").IsPrimaryKey(PKGenerationStrategy.SequentialGuid);
                this.MapsToField(() => Comment, databasePropertyName: "Comment");

                hasInit = true;
            }
        }

        public Guid Id { get; set; }
        public string Comment { get; set; }
    }
}
