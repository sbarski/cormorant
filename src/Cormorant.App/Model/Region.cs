﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cormorant.App.Model
{
    public class Region : IDatabaseModel
    {
        public Region()
        {
            this.MapsToTable("Region");
            this.MapsToField(() => Id, "RegionID");
            this.MapsToField(() => RegionDescription, "RegionDescription");
        }

        public int Id { get; set; }
        public string RegionDescription { get; set; }
    }
}
