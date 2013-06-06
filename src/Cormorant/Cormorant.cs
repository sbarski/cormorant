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

    public enum PKGenerationStrategy
    {
        Identity = 1,
        SequentialGuid = 2,
        NewGuid = 3
    }

    public class DatabaseField
    {
        public DatabaseField(string assembly, string clrName, string dbName)
        {
            Assembly = assembly;
            CLRName = clrName;
            DbName = dbName;
            IsPrimaryKey = false;
            IsRowVersion = false;
        }

        protected internal string Assembly { get; set; }
        protected internal string CLRName { get; set; }
        protected internal string DbName { get; set; }
        protected internal bool IsPrimaryKey { get; set; }
        protected internal bool IsRowVersion { get; set; }
        protected internal PKGenerationStrategy PKGenerationStrategy { get; set; }
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
        private static List<DatabaseField> _nameMappings = new List<DatabaseField>(); //object fully qualified (name), from model (name), to database (name)
        private static Dictionary<string, string> _tableMappings = new Dictionary<string, string>(); //object fully qualified (name) to database (name)

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

                var mappings = _nameMappings
                                    .Where(m => string.Equals(m.Assembly, databaseModel.GetType().FullName))
                                    .ToDictionary(m => m.CLRName, n => n.DbName);

                var sqlExpression = string.Format(SqlExpressions.SelectClause, tableName);

                var command = new SqlCommand(sqlExpression, connection);

                using (var result = command.ExecuteReader())
                {
                    while (result.Read())
                    {
                        var model = result.ConvertDataToModel<T>(databaseModel, mappings);

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

                connection.Open();

                using (var tx = connection.BeginTransaction(transactionName))
                {
                    try
                    {
                        var primaryKey = GetPrimaryKey(databaseModel);
                        
                        if (primaryKey == null)
                        {
                            throw new ConfigurationErrorsException(string.Format("A primary key was not defined for type: {0}", databaseModel.GetType().FullName));
                        }

                        var parameters = GenerateSetMethod(databaseModel);

                        var sqlAssignment = string.Join(", ", parameters.Where(m => !string.Equals(m.ParameterName, "@" + primaryKey.DbName)).Select(param => string.Format(SqlExpressions.AssignClause, param.ParameterName.TrimStart(new []{'@'}), param)));

                        var sqlStatement = string.Format("UPDATE {0} SET {1} WHERE [{2}] = @{2}", tableName, sqlAssignment, primaryKey.DbName);

                        var command = new SqlCommand(sqlStatement, connection, tx);
                        
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

                connection.Open();

                using (var tx = connection.BeginTransaction(transactionName))
                {
                    try
                    {
                        var primaryKey =  GetPrimaryKey(databaseModel);

                        if (primaryKey == null)
                        {
                            throw new ConfigurationErrorsException(string.Format("A primary key was not defined for type: {0}", databaseModel.GetType().FullName));
                        }

                        var parameters = GenerateSetMethod(databaseModel);
                                
                        var pk = parameters.Single(m => string.Equals(m.ParameterName, "@" + primaryKey.DbName));

                        switch (primaryKey.PKGenerationStrategy)
                        {
                            //Remove the PK/Identity Parameter altogether
                            case PKGenerationStrategy.Identity:
                                parameters = parameters.Except(new[] {pk}).ToArray();
                                break;

                            case PKGenerationStrategy.NewGuid:
                                pk.Value = "NEWID()";
                                pk.SqlDbType = SqlDbType.UniqueIdentifier;
                                break;

                            case PKGenerationStrategy.SequentialGuid:
                                pk.Value = "NEWSEQUENTIALID()";
                                pk.SqlDbType = SqlDbType.UniqueIdentifier;
                                break;

                        }

                        var sqlStatement = string.Format("INSERT INTO {0} ({1}) VALUES ({2})", 
                            tableName, 
                            string.Join(", ", parameters.Select(m => m.ParameterName.TrimStart(new []{'@'}))), 
                            string.Join(", ", parameters.Select(m => m.ParameterName)));

                        var command = new SqlCommand(sqlStatement, connection, tx);
                        
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
        private static SqlParameter[] GenerateSetMethod<T>(T databaseModel) where T : IDatabaseModel
        {
            var sqlBuilder = new List<string>();

            var parameters = new List<SqlParameter>();

            var fieldMappings = _nameMappings.Where(x => string.Equals(x.Assembly, databaseModel.GetType().FullName))
                                             .ToDictionary(m => m.CLRName, n => n.DbName);

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
            }
            
            return parameters.ToArray();
        }

        private static DatabaseField GetPrimaryKey(IDatabaseModel databaseModel)
        {
            var primaryKeyField = _nameMappings.FirstOrDefault(m => string.Equals(m.Assembly, databaseModel.GetType().FullName) && m.IsPrimaryKey);

            if (primaryKeyField == null)
            {
                throw new ArgumentNullException("Could not find a mapping for type: {0}", databaseModel.GetType().FullName);
            }

            return primaryKeyField;
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

        public static DatabaseField IsPrimaryKey(this DatabaseField databaseField, PKGenerationStrategy pkGenerationStrategy)
        {
            if (databaseField.IsRowVersion)
            {
                throw new ConfigurationErrorsException(string.Format("Primary Key and Row Version cannot be true for the same property for: {0}", databaseField.Assembly));
            }

            databaseField.IsPrimaryKey = true;
            databaseField.PKGenerationStrategy = pkGenerationStrategy;

            return databaseField;
        }

        public static DatabaseField IsRowVersion(this DatabaseField databaseField)
        {
            if (databaseField.IsPrimaryKey)
            {
                throw new ConfigurationErrorsException(string.Format("Primary Key and Row Version cannot be true for the same property for: {0}", databaseField.Assembly));
            }

            databaseField.IsRowVersion = true;

            return databaseField;
        }

        public static DatabaseField MapsToField(this IDatabaseModel databaseModel, Expression<Func<Object>> property, string databasePropertyName) 
        {
            var propertyName = (property.Body as MemberExpression ?? ((UnaryExpression)property.Body).Operand as MemberExpression).Member.Name;

            var field = _nameMappings.FirstOrDefault(m => string.Equals(m.Assembly, databaseModel.GetType().FullName) && string.Equals(m.CLRName, propertyName));

            if (field != null)
            {
                return field;
            }

            var dbField = new DatabaseField(databaseModel.GetType().FullName, propertyName, databasePropertyName);

            _nameMappings.Add(dbField);

            return dbField;
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

        public static string AssignClause
        {
            get { return "[{0}] = {1}"; }
        }

        public static string WhereClause
        {
            get { return "WHERE [{0}] = @{1}"; }
        }
    };
}
