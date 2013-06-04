using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Cormorant;

namespace Cormorant.Test
{
    [TestClass]
    public class DatabaseTest
    {
        private Database _database;

        [TestInitialize]
        public void TestInitialize()
        {
            _database = new Database("(local)", string.Empty, string.Empty, "Northwind");
        }

        [TestMethod]
        public void CanConnectToDatabase()
        {
            var database = new Database("(local)", string.Empty, string.Empty, "Northwind");

            Assert.IsTrue(database.CanConnectToDatabase());
        }

        [TestMethod]
        public void CanReadRegionTable()
        {
            //var region = _database.
        }
    }
}
