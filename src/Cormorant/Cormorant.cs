using System;
using System.Collections;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Cormorant
{
    public interface IDatabaseModel
    {

    }

    public class Database
    {
        public static string ConnectionString { get; private set; }

        public Database(string connectionStringName)
        {
            var connectionString = ConfigurationManager.ConnectionStrings["connectionStringName"];

            ConnectionString = connectionString.ConnectionString;

            if (ConnectionString == null)
            {
                throw new ArgumentException("The supplied exception string was not found", "connectionStringName");
            }
        }

        public Database(string dataSource, string username, string password, string database, bool usingIntegratedSecurity = true)
        {
            var connectionStringBuilder = new SqlConnectionStringBuilder
            {
                UserID = username,
                DataSource = dataSource,
                Password = password,
                InitialCatalog = database,
                IntegratedSecurity = usingIntegratedSecurity
            };

            ConnectionString = connectionStringBuilder.ConnectionString;
        }

        public bool CanConnectToDatabase()
        {
            bool canConnectToDatabase;

            try
            {
                using (var connection = new SqlConnection(ConnectionString))
                {
                    connection.Open();

                    canConnectToDatabase = true;
                }
            }
            catch (Exception)
            {
                canConnectToDatabase = false;
            }

            return canConnectToDatabase;
        }
    }

    public static class DatabaseModelHelper
    {
        private static List<Tuple<string, string, string>> _nameMappings; //object fully qualified (name), from model (name), to database (name)
        private static Dictionary<string, string> _tableMappings; //object fully qualified (name) to database (name)

        public static IEnumerable GetAll<T>(this T databaseModel) where T: IDatabaseModel
        {
            if (string.IsNullOrEmpty(Database.ConnectionString))
            {
                throw new NoNullAllowedException("The connection string is null or empty");
            }

            using (var connection = new SqlConnection(Database.ConnectionString))
            {
                connection.Open();

                var tableName = GetTableName(databaseModel);

                var sqlExpression = string.Format(SqlExpressions.WhereClause, tableName);

                var command = new SqlCommand(sqlExpression, connection);

                using (var result = command.ExecuteReader())
                {
                    while (result.Read())
                    {
                        var model = result.ConvertTo<T>(databaseModel, GetFieldNameMappings(databaseModel));

                        yield return model;
                    }
                }
            }
        }

        public static void Save<T>(this T databaseModel) where T : IDatabaseModel
        {
            if (string.IsNullOrEmpty(Database.ConnectionString))
            {
                throw new NoNullAllowedException("The connection string is null or empty");
            }

            using (var connection = new SqlConnection(Database.ConnectionString))
            {
                connection.Open();

                var tableName = GetTableName(databaseModel);

                //var sqlExpression = string.Format()

            }
        }

        private static Dictionary<string, string> GetFieldNameMappings(IDatabaseModel databaseModel)
        {
            var nameMapping = _nameMappings
                .Where(m => string.Equals(m.Item1, databaseModel.GetType().FullName))
                .ToDictionary(c => c.Item2, k => k.Item3);

            return nameMapping;
        }

        private static string GetTableName(IDatabaseModel databaseModel)
        {
            var tableMappingName = _tableMappings.FirstOrDefault(m => string.Equals(m.Key, databaseModel.GetType().FullName)).Value;

            if (!string.IsNullOrEmpty(tableMappingName))
            {
                return tableMappingName;
            }

            return databaseModel.GetType().Name;
        }

        public static IDatabaseModel MapsToField(this IDatabaseModel databaseModel, Expression<Func<Object>> property, string databasePropertyName) 
        {
            if (_nameMappings == null)
            {
                _nameMappings = new List<Tuple<string, string, string>>();
            }



            var propertyName = (property.Body as MemberExpression ?? ((UnaryExpression)property.Body).Operand as MemberExpression).Member.Name;

            var tuple = new Tuple<string, string, string>(databaseModel.GetType().FullName, propertyName, databasePropertyName);

            if (_nameMappings.Contains(tuple))
            {
                return databaseModel;
            }

            _nameMappings.Add(tuple);
            
            return databaseModel;
        }

        public static IDatabaseModel MapsToTable(this IDatabaseModel databaseModel, string databaseName)
        {
            if (_tableMappings == null)
            {
                _tableMappings = new Dictionary<string, string>();
            }

            if (!_tableMappings.ContainsKey(databaseModel.GetType().FullName) && !_tableMappings.ContainsValue(databaseName))
            {
                _tableMappings.Add(databaseModel.GetType().FullName, databaseName);
            }

            return databaseModel;
        }
    }

    internal static class ConversionHelper
    {
        public static object ConvertTo<T>(this IDataReader dataReader, IDatabaseModel databaseModel, Dictionary<string, string> nameMappings) where T: IDatabaseModel
        {
            var model = Activator.CreateInstance<T>();

            for (int i = 0; i < dataReader.FieldCount; i++)
            {
                var propertyName = dataReader.GetName(i);

                if (nameMappings != null)
                {
                    var fieldName = nameMappings.FirstOrDefault(m => string.Equals(m.Value, propertyName)).Key;

                    if (!string.IsNullOrEmpty(fieldName))
                    {
                        propertyName = fieldName;
                    }
                }

                var propertyValue = DBNull.Value.Equals(dataReader[i]) ? null : dataReader.GetValue(i);

                var prop = model.GetType().GetProperty(propertyName);

                if (prop != null && prop.CanWrite)
                {
                    prop.SetValue(model, propertyValue, null);
                }
            }

            return model;
        }
    }

    static class SqlExpressions
    {
        public static string WhereClause { get { return "SELECT * FROM {0}"; } }

        public static string UpdateClause { get { return "UPDATE {0} SET {1} WHERE {2} = @{3}"; } }
    }
}
