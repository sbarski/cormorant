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
        private static List<Tuple<string, string, string>> _nameMappings = new List<Tuple<string, string, string>>(); //object fully qualified (name), from model (name), to database (name)
        private static Dictionary<string, string> _tableMappings = new Dictionary<string, string>(); //object fully qualified (name) to database (name)
        private static List<string> _primaryKeyMappings = new List<string>(); //fully qualified name 

        public static IEnumerable<T> GetAll<T>(this T databaseModel) where T: IDatabaseModel
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
                        var model = result.ConvertDataToModel<T>(databaseModel, GetFieldNameMappings(databaseModel));

                        yield return model;
                    }
                }
            }
        }

        public static void Update<T>(this T databaseModel, string transactionName = "") where T : IDatabaseModel
        {
            if (string.IsNullOrEmpty(Database.ConnectionString))
            {
                throw new NoNullAllowedException("The connection string is null or empty");
            }

            using (var connection = new SqlConnection(Database.ConnectionString))
            {
                var tableName = GetTableName(databaseModel);

                var setStatements = GenerateSetMethod(databaseModel);

                var updateStatement = string.Format(SqlExpressions.UpdateClause, tableName, setStatements);

                connection.Open();

                using (var tx = connection.BeginTransaction(transactionName))
                {
                    try
                    {
                        var command = new SqlCommand(updateStatement, connection, tx);

                        command.ExecuteNonQuery();

                        tx.Commit();
                    }
                    catch (SqlException e)
                    {
                        tx.Rollback();
                    }
                }

                connection.Close();
            }
        }

        private static string GenerateSetMethod<T>(T databaseModel) where T : IDatabaseModel
        {
            var setMethods = new StringBuilder();
            var fieldMappings = _nameMappings.Where(x => string.Equals(x.Item1, databaseModel.GetType().FullName))
                                             .ToDictionary(m => m.Item2, n => n.Item3);

            var propertiesToUpdate = databaseModel.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public);

            foreach (var field in propertiesToUpdate)
            {
                var dbFieldName = field.Name;

                if (fieldMappings.ContainsKey(field.Name))
                {
                    dbFieldName = fieldMappings.SingleOrDefault(m => string.Equals(m.Key, dbFieldName)).Value;
                }

                setMethods.Append(string.Format(SqlExpressions.SetClause + " ", dbFieldName, field.GetValue(databaseModel)));
            }

            return setMethods.ToString();
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

        public static IDatabaseModel MapsToField(this IDatabaseModel databaseModel, Expression<Func<Object>> property, string databasePropertyName, bool isPrimaryKey = false) 
        {
            var propertyName = (property.Body as MemberExpression ?? ((UnaryExpression)property.Body).Operand as MemberExpression).Member.Name;

            var tuple = new Tuple<string, string, string>(databaseModel.GetType().FullName, propertyName, databasePropertyName);

            if (_nameMappings.Contains(tuple))
            {
                return databaseModel;
            }

            var fullPropertyName = string.Format("{0}.{1}", databaseModel.GetType().FullName, propertyName);
            if (isPrimaryKey && !_primaryKeyMappings.Contains(fullPropertyName))
            {
                _primaryKeyMappings.Add(fullPropertyName);
            }

            _nameMappings.Add(tuple);
            
            return databaseModel;
        }

        public static IDatabaseModel MapsToTable(this IDatabaseModel databaseModel, string databaseName)
        {
            if (!_tableMappings.ContainsKey(databaseModel.GetType().FullName) && !_tableMappings.ContainsValue(databaseName))
            {
                _tableMappings.Add(databaseModel.GetType().FullName, databaseName);
            }

            return databaseModel;
        }
    }

    internal static class ConversionHelper
    {
        public static T ConvertDataToModel<T>(this IDataReader dataReader, IDatabaseModel databaseModel, Dictionary<string, string> nameMappings) where T: IDatabaseModel
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

        public static string UpdateClause { get { return "UPDATE {0} {1}"; } }

        public static string SetClause { get { return "SET {0} = {1}"; } }
    }
}
