using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Cormorant;

namespace Cormorant.Test
{
    [TestClass]
    public class DatabaseTest
    {
        [TestMethod]
        public void CanConnectToDatabase()
        {
            var database = new Database("(local)", string.Empty, string.Empty, "GHD.Pivot");

            Assert.IsTrue(database.CanConnectToDatabase());
        }
    }
}
