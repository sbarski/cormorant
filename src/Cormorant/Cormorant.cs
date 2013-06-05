using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
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
        private static Dictionary<string, string> _primaryKeyMappings = new Dictionary<string, string>(); //object fully qualified name, database (name) 
        private static Dictionary<string, string> _rowVersionMappings = new Dictionary<string, string>(); //object fully qualified name, database (name) 

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

                var sqlExpression = string.Format(SqlExpressions.SelectClause, tableName);

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

                var updateStatement = string.Format(SqlExpressions.UpdateClause, tableName);

                connection.Open();

                using (var tx = connection.BeginTransaction(transactionName))
                {
                    try
                    {
                        string sqlSetStatement;
                        var parameters = GenerateSetMethod(databaseModel, out sqlSetStatement);

                        var sql = string.Format("{0} {1}", updateStatement, sqlSetStatement);

                        var command = new SqlCommand(sql, connection, tx);
                        command.Parameters.AddRange(parameters);

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

        public static void Insert<T>(this T databaseModel, string transactionName = "") where T : IDatabaseModel
        {
            if (string.IsNullOrEmpty(Database.ConnectionString))
            {
                throw new NoNullAllowedException("The connection string is null or empty");
            }

            using (var connection = new SqlConnection(Database.ConnectionString))
            {
                var tableName = GetTableName(databaseModel);

                var updateStatement = string.Format(SqlExpressions.UpdateClause, tableName);

                connection.Open();

                using (var tx = connection.BeginTransaction(transactionName))
                {
                    try
                    {
                        string sqlSetStatement;
                        var parameters = GenerateSetMethod(databaseModel, out sqlSetStatement);

                        var sql = string.Format("{0} {1}", updateStatement, sqlSetStatement);

                        var command = new SqlCommand(sql, connection, tx);
                        command.Parameters.AddRange(parameters);

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

        /// <summary>
        /// Generates set methods based on the CLR model 
        /// </summary>x
        private static SqlParameter[] GenerateSetMethod<T>(T databaseModel, out string sql) where T : IDatabaseModel
        {
            var sqlBuilder = new List<string>();

            var parameters = new List<SqlParameter>();

            var fieldMappings = _nameMappings.Where(x => string.Equals(x.Item1, databaseModel.GetType().FullName))
                                             .ToDictionary(m => m.Item2, n => n.Item3);

            var fullName = databaseModel.GetType().FullName;

            var primaryKeyName = GetPrimaryKey(fullName);

            var rowVersion = GetRowVersion(fullName);

            var propertiesToUpdate = databaseModel
                    .GetType()
                    .GetProperties(BindingFlags.Instance | BindingFlags.Public);

            foreach (var property in propertiesToUpdate)
            {
                var dbFieldName = property.Name;

                if (fieldMappings.ContainsKey(property.Name))
                {
                    dbFieldName = fieldMappings.SingleOrDefault(m => string.Equals(m.Key, dbFieldName)).Value;
                }

                object propertyValue = property.GetValue(databaseModel);
                var parameter = new SqlParameter(string.Format("@{0}", dbFieldName), propertyValue);

                parameters.Add(parameter);

                //if the primary key was empty then try to guess what it could be
                if (string.IsNullOrEmpty(primaryKeyName) && (string.Equals(property.Name, "Id", StringComparison.InvariantCulture) || property.Name.EndsWith("Id", StringComparison.InvariantCulture)))
                {
                    primaryKeyName = dbFieldName;
                }

                if (!string.Equals(dbFieldName, primaryKeyName))
                {
                    sqlBuilder.Add(string.Format(SqlExpressions.SetClause, dbFieldName, dbFieldName));
                }
            }

            if (string.IsNullOrEmpty(primaryKeyName))
            {
                throw new ConfigurationErrorsException(string.Format("Primary key not defined, not found or misconfigured for: {0}", databaseModel.GetType().FullName));
            }

            var where = string.Format(SqlExpressions.WhereClause, primaryKeyName, primaryKeyName);

            if (!string.IsNullOrEmpty(rowVersion))
            {
                where = string.Format("{0} AND [{1}] = @{2}", where, rowVersion, rowVersion);
            }


            sql = string.Format("SET {0} {1}", string.Join(", ", sqlBuilder), where);

            return parameters.ToArray();
        }

        public static string GetRowVersion(string fullName)
        {
            if (_rowVersionMappings.ContainsKey(fullName))
            {
                var rowVersion = _rowVersionMappings[fullName];

                return rowVersion;
            }

            return null;
        }

        private static string GetPrimaryKey(string fullName)
        {
            if (_primaryKeyMappings.ContainsKey(fullName))
            {
                var modelPrimaryKey = _primaryKeyMappings[fullName];

                return modelPrimaryKey;
            }

            return null;
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

        public static void MapsToField(this IDatabaseModel databaseModel, Expression<Func<Object>> property, string databasePropertyName, bool isPrimaryKey = false, bool isRowVersion = false) 
        {
            if (isRowVersion && isPrimaryKey)
            {
                throw new ConfigurationErrorsException(string.Format("Primary Key and Row Version cannot be true for the same property for: {0}", databaseModel.GetType().FullName));
            }

            var propertyName = (property.Body as MemberExpression ?? ((UnaryExpression)property.Body).Operand as MemberExpression).Member.Name;

            var fullName = databaseModel.GetType().FullName;

            if (_nameMappings.Any(m => string.Equals(m.Item1, fullName)))
            {
                return;
            }

            var tuple = new Tuple<string, string, string>(fullName, propertyName, databasePropertyName);

            if (isPrimaryKey && !_primaryKeyMappings.ContainsKey(fullName))
            {
                _primaryKeyMappings.Add(fullName, databasePropertyName);
            }

            if (isRowVersion && !_rowVersionMappings.ContainsKey(fullName))
            {
                _rowVersionMappings.Add(fullName, databasePropertyName);
            }

            _nameMappings.Add(tuple);
        }
        
        public static void MapsToTable(this IDatabaseModel databaseModel, string databaseName)
        {
            if (!_tableMappings.ContainsKey(databaseModel.GetType().FullName) && !_tableMappings.ContainsValue(databaseName))
            {
                _tableMappings.Add(databaseModel.GetType().FullName, databaseName);
            }
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

    internal static class SqlExpressions
    {
        public static string SelectClause
        {
            get { return "SELECT * FROM {0}"; }
        }

        public static string UpdateClause
        {
            get { return "UPDATE {0}"; }
        }

        public static string SetClause
        {
            get { return "[{0}] = @{1}"; }
        }

        public static string WhereClause
        {
            get { return "WHERE [{0}] = @{1}"; }
        }

        public static string InsertClause
        {
            get { return "INSERT INTO {0}"; }
        }
    };
}
