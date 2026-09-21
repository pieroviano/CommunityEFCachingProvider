// Copyright (c) Microsoft Corporation.  All rights reserved.

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Data.Common.CommandTrees;
using System.Data.SqlClient;
using System.Transactions;
using EFCachingProvider.Caching;
using EFCachingProvider.Tests.Infrastructure;
using Xunit;

namespace EFCachingProvider.Tests
{
    public class EFCachingCommandTests
    {
        private readonly FakeDbConnection store = new FakeDbConnection();
        private readonly InMemoryCache cache = new InMemoryCache();
        private readonly EFCachingConnection connection;

        public EFCachingCommandTests()
        {
            this.connection = TestModel.CachingConnection(this.store, this.cache);
        }

        private static List<object[]> ReadAll(DbDataReader reader)
        {
            var rows = new List<object[]>();
            using (reader)
            {
                while (reader.Read())
                {
                    var values = new object[reader.FieldCount];
                    reader.GetValues(values);
                    rows.Add(values);
                }
            }

            return rows;
        }

        private EFCachingCommand Query(FakeDbCommand storeCommand, string text = "SELECT * FROM Customers", DbCommandTree tree = null)
        {
            return TestModel.Command(this.connection, tree ?? TestModel.Query(TestModel.Customers), storeCommand, text);
        }

        private EFCachingCommand DeleteCustomers(FakeDbCommand storeCommand)
        {
            return TestModel.Command(this.connection, TestModel.Delete(TestModel.Customers), storeCommand, "DELETE FROM Customers");
        }

        private static void AddParameter(DbCommand command, string name, object value)
        {
            DbParameter p = command.CreateParameter();
            p.ParameterName = name;
            p.Value = value;
            command.Parameters.Add(p);
        }

        [Fact]
        public void SecondExecution_IsServedFromTheCache()
        {
            var storeCommand = new FakeDbCommand { Result = FakeDbCommand.Table("Id,Name", new object[] { 1, "a" }) };
            var command = this.Query(storeCommand);

            var first = ReadAll(command.ExecuteReader());
            var second = ReadAll(command.ExecuteReader());

            Assert.Equal(1, storeCommand.ReaderExecutions);
            Assert.Equal(first, second);
            Assert.Equal(1, this.cache.CacheHits);
        }

        [Fact]
        public void NoCache_AlwaysExecutesAgainstTheStore()
        {
            this.connection.Cache = null;
            var storeCommand = new FakeDbCommand();
            var command = this.Query(storeCommand);

            ReadAll(command.ExecuteReader());
            ReadAll(command.ExecuteReader());

            Assert.Equal(2, storeCommand.ReaderExecutions);
        }

        [Fact]
        public void NoCachingPolicy_AlwaysExecutesAgainstTheStore()
        {
            this.connection.CachingPolicy = CachingPolicy.NoCaching;
            var storeCommand = new FakeDbCommand();
            var command = this.Query(storeCommand);

            ReadAll(command.ExecuteReader());
            ReadAll(command.ExecuteReader());

            Assert.Equal(2, storeCommand.ReaderExecutions);
            Assert.Equal(0, this.cache.Count);
        }

        [Fact]
        public void NonDeterministicQuery_IsNeverCached()
        {
            var storeCommand = new FakeDbCommand();
            var command = this.Query(storeCommand, "SELECT GETDATE()", TestModel.QueryCalling(TestModel.Customers, "GETDATE"));

            ReadAll(command.ExecuteReader());
            ReadAll(command.ExecuteReader());

            Assert.Equal(2, storeCommand.ReaderExecutions);
        }

        [Fact]
        public void OutputParameter_DisablesCaching()
        {
            var storeCommand = new FakeDbCommand();
            var command = this.Query(storeCommand);
            command.Parameters.Add(new SqlParameter("out", SqlDbType.Int) { Direction = ParameterDirection.Output });

            ReadAll(command.ExecuteReader());
            ReadAll(command.ExecuteReader());

            Assert.Equal(2, storeCommand.ReaderExecutions);
        }

        [Fact]
        public void DifferentParameterValues_AreCachedSeparately()
        {
            var storeCommand = new FakeDbCommand { Result = FakeDbCommand.Table("Id", new object[] { 1 }) };
            var command = this.Query(storeCommand, "SELECT * FROM Customers WHERE Id = @p0");
            AddParameter(command, "p0", 1);
            ReadAll(command.ExecuteReader());

            command.Parameters[0].Value = 2;
            storeCommand.Result = FakeDbCommand.Table("Id", new object[] { 2 });
            var rows = ReadAll(command.ExecuteReader());

            Assert.Equal(2, storeCommand.ReaderExecutions);
            Assert.Equal(2, rows[0][0]);
        }

        [Fact]
        public void ParameterWhoseNameIsAPrefixOfAnother_DoesNotCollide()
        {
            // "@p1" is a prefix of "@p10": substituting values into the command text used to rewrite both,
            // so queries differing only in @p10 shared a cache entry and returned each other's rows.
            var storeCommand = new FakeDbCommand();
            var command = this.Query(storeCommand, "SELECT * FROM Customers WHERE Id = @p1 OR Id = @p10");
            AddParameter(command, "p1", 1);
            AddParameter(command, "p10", 10);
            ReadAll(command.ExecuteReader());

            command.Parameters[1].Value = 11;
            ReadAll(command.ExecuteReader());

            Assert.Equal(2, storeCommand.ReaderExecutions);
        }

        [Fact]
        public void ParameterNamedWithItsPrefix_IsPartOfTheKey()
        {
            // SqlClient accepts "@p0" as well as "p0"; the value must count either way.
            var storeCommand = new FakeDbCommand();
            var command = this.Query(storeCommand, "SELECT * FROM Customers WHERE Id = @p0");
            AddParameter(command, "@p0", 1);
            ReadAll(command.ExecuteReader());

            command.Parameters[0].Value = 2;
            ReadAll(command.ExecuteReader());

            Assert.Equal(2, storeCommand.ReaderExecutions);
        }

        [Fact]
        public void ParameterValuesThatFormatAlike_AreCachedSeparately()
        {
            var storeCommand = new FakeDbCommand();
            var command = this.Query(storeCommand, "SELECT * FROM Customers WHERE Stamp = @p0");
            AddParameter(command, "p0", new DateTime(2020, 1, 1, 10, 0, 0, 1));
            ReadAll(command.ExecuteReader());

            command.Parameters[0].Value = new DateTime(2020, 1, 1, 10, 0, 0, 2);
            ReadAll(command.ExecuteReader());

            command.Parameters[0].Value = new byte[] { 1 };
            ReadAll(command.ExecuteReader());

            command.Parameters[0].Value = new byte[] { 2 };
            ReadAll(command.ExecuteReader());

            Assert.Equal(4, storeCommand.ReaderExecutions);
        }

        [Fact]
        public void NullAndEmptyStringParameters_AreCachedSeparately()
        {
            var storeCommand = new FakeDbCommand();
            var command = this.Query(storeCommand, "SELECT * FROM Customers WHERE Name = @p0");
            AddParameter(command, "p0", DBNull.Value);
            ReadAll(command.ExecuteReader());

            command.Parameters[0].Value = string.Empty;
            ReadAll(command.ExecuteReader());

            Assert.Equal(2, storeCommand.ReaderExecutions);
        }

        [Fact]
        public void EqualParameterValues_ShareTheCacheEntry()
        {
            var storeCommand = new FakeDbCommand();
            var command = this.Query(storeCommand, "SELECT * FROM Customers WHERE Name = @p0");
            AddParameter(command, "p0", "O'Brien");
            ReadAll(command.ExecuteReader());
            ReadAll(command.ExecuteReader());

            Assert.Equal(1, storeCommand.ReaderExecutions);
        }

        [Fact]
        public void PolicyRowLimits_AreHonoured()
        {
            this.connection.CachingPolicy = new CustomCachingPolicy { MinCacheableRows = 2, MaxCacheableRows = 3 };
            var storeCommand = new FakeDbCommand { Result = FakeDbCommand.Table("Id", new object[] { 1 }) };
            var command = this.Query(storeCommand);

            ReadAll(command.ExecuteReader());
            ReadAll(command.ExecuteReader());
            Assert.Equal(2, storeCommand.ReaderExecutions);

            storeCommand.Result = FakeDbCommand.Table("Id", new object[] { 1 }, new object[] { 2 });
            ReadAll(command.ExecuteReader());
            ReadAll(command.ExecuteReader());
            Assert.Equal(3, storeCommand.ReaderExecutions);
        }

        [Fact]
        public void CommittedModification_InvalidatesDependentQueries()
        {
            var queryCommand = new FakeDbCommand();
            ReadAll(this.Query(queryCommand).ExecuteReader());

            using (DbTransaction tx = this.connection.BeginTransaction())
            {
                var delete = this.DeleteCustomers(new FakeDbCommand());
                delete.Transaction = tx;
                delete.ExecuteNonQuery();
                tx.Commit();
            }

            ReadAll(this.Query(queryCommand).ExecuteReader());
            Assert.Equal(2, queryCommand.ReaderExecutions);
        }

        [Fact]
        public void CommittedModification_KeepsUnrelatedQueriesCached()
        {
            var ordersCommand = new FakeDbCommand();
            ReadAll(this.Query(ordersCommand, "SELECT * FROM Orders", TestModel.Query(TestModel.Orders)).ExecuteReader());

            using (DbTransaction tx = this.connection.BeginTransaction())
            {
                var delete = this.DeleteCustomers(new FakeDbCommand());
                delete.Transaction = tx;
                delete.ExecuteNonQuery();
                tx.Commit();
            }

            ReadAll(this.Query(ordersCommand, "SELECT * FROM Orders", TestModel.Query(TestModel.Orders)).ExecuteReader());
            Assert.Equal(1, ordersCommand.ReaderExecutions);
        }

        [Fact]
        public void QueryInsideAModifyingTransaction_SeesTheStore_NotTheCache()
        {
            // After a write, a transaction must read its own changes; a cached pre-write result is wrong.
            var queryCommand = new FakeDbCommand();
            ReadAll(this.Query(queryCommand).ExecuteReader());

            using (DbTransaction tx = this.connection.BeginTransaction())
            {
                var delete = this.DeleteCustomers(new FakeDbCommand());
                delete.Transaction = tx;
                delete.ExecuteNonQuery();

                var query = this.Query(queryCommand);
                query.Transaction = tx;
                ReadAll(query.ExecuteReader());

                tx.Rollback();
            }

            Assert.Equal(2, queryCommand.ReaderExecutions);
        }

        [Fact]
        public void RolledBackTransaction_LeavesNoUncommittedRowsInTheCache()
        {
            var queryCommand = new FakeDbCommand { Result = FakeDbCommand.Table("Id") };
            using (DbTransaction tx = this.connection.BeginTransaction())
            {
                var delete = this.DeleteCustomers(new FakeDbCommand());
                delete.Transaction = tx;
                delete.ExecuteNonQuery();

                var query = this.Query(queryCommand);
                query.Transaction = tx;
                ReadAll(query.ExecuteReader());

                tx.Rollback();
            }

            queryCommand.Result = FakeDbCommand.Table("Id", new object[] { 1 });
            var rows = ReadAll(this.Query(queryCommand).ExecuteReader());

            Assert.Single(rows);
        }

        [Fact]
        public void ReadOnlyTransaction_StillUsesTheCache()
        {
            var queryCommand = new FakeDbCommand();
            ReadAll(this.Query(queryCommand).ExecuteReader());

            using (DbTransaction tx = this.connection.BeginTransaction())
            {
                var query = this.Query(queryCommand);
                query.Transaction = tx;
                ReadAll(query.ExecuteReader());
                tx.Commit();
            }

            Assert.Equal(1, queryCommand.ReaderExecutions);
        }

        [Fact]
        public void Transaction_IsPassedToTheStoreCommand()
        {
            var storeCommand = new FakeDbCommand();
            var command = this.DeleteCustomers(storeCommand);

            using (var tx = (EFCachingTransaction)this.connection.BeginTransaction())
            {
                command.Transaction = tx;
                Assert.Same(tx.WrappedTransaction, storeCommand.Transaction);
                Assert.Same(tx, command.Transaction);

                command.Transaction = null;
                Assert.Null(storeCommand.Transaction);
            }
        }

        [Fact]
        public void AmbientTransaction_CommittedModification_InvalidatesDependentQueries()
        {
            var queryCommand = new FakeDbCommand();
            ReadAll(this.Query(queryCommand).ExecuteReader());

            using (var scope = new TransactionScope())
            {
                this.connection.Open();
                this.DeleteCustomers(new FakeDbCommand()).ExecuteNonQuery();
                scope.Complete();
            }

            this.connection.Close();
            ReadAll(this.Query(queryCommand).ExecuteReader());
            Assert.Equal(2, queryCommand.ReaderExecutions);
        }

        [Fact]
        public void AmbientTransaction_OnAnAlreadyOpenConnection_InvalidatesOnCommit()
        {
            // EntityConnection enlists an already open store connection with EnlistTransaction, not Open.
            this.connection.Open();
            var queryCommand = new FakeDbCommand();
            ReadAll(this.Query(queryCommand).ExecuteReader());

            using (var scope = new TransactionScope())
            {
                this.connection.EnlistTransaction(Transaction.Current);
                this.DeleteCustomers(new FakeDbCommand()).ExecuteNonQuery();
                scope.Complete();
            }

            ReadAll(this.Query(queryCommand).ExecuteReader());
            Assert.Equal(2, queryCommand.ReaderExecutions);
            Assert.NotNull(this.store.EnlistedTransaction);
        }

        [Fact]
        public void AfterAnAmbientTransactionCompletes_LaterModificationsStillInvalidate()
        {
            // The enlistment of a finished transaction must not keep swallowing later modifications.
            using (var scope = new TransactionScope())
            {
                this.connection.Open();
                this.DeleteCustomers(new FakeDbCommand()).ExecuteNonQuery();
                scope.Complete();
            }

            var queryCommand = new FakeDbCommand();
            ReadAll(this.Query(queryCommand).ExecuteReader());
            ReadAll(this.Query(queryCommand).ExecuteReader());
            Assert.Equal(1, queryCommand.ReaderExecutions);

            using (DbTransaction tx = this.connection.BeginTransaction())
            {
                var delete = this.DeleteCustomers(new FakeDbCommand());
                delete.Transaction = tx;
                delete.ExecuteNonQuery();
                tx.Commit();
            }

            ReadAll(this.Query(queryCommand).ExecuteReader());
            Assert.Equal(2, queryCommand.ReaderExecutions);
        }

        [Fact]
        public void AmbientTransaction_RolledBack_DoesNotInvalidate()
        {
            var queryCommand = new FakeDbCommand();
            ReadAll(this.Query(queryCommand).ExecuteReader());

            using (new TransactionScope())
            {
                this.connection.Open();
                this.DeleteCustomers(new FakeDbCommand()).ExecuteNonQuery();
            }

            this.connection.Close();
            ReadAll(this.Query(queryCommand).ExecuteReader());
            Assert.Equal(1, queryCommand.ReaderExecutions);
        }

        [Fact]
        public void ExecuteScalar_And_ExecuteNonQuery_GoToTheStore()
        {
            var storeCommand = new FakeDbCommand { ScalarResult = 42 };
            var command = this.Query(storeCommand);

            Assert.Equal(42, command.ExecuteScalar());
            Assert.Equal(1, command.ExecuteNonQuery());
            Assert.Equal(1, storeCommand.NonQueryExecutions);
        }

        [Fact]
        public void StatisticsCounters_Advance()
        {
            int hits = EFCachingCommand.CacheHits;
            int misses = EFCachingCommand.CacheMisses;
            int adds = EFCachingCommand.CacheAdds;
            int cacheable = EFCachingCommand.CacheableCommands;
            var command = this.Query(new FakeDbCommand());

            ReadAll(command.ExecuteReader());
            ReadAll(command.ExecuteReader());

            Assert.Equal(hits + 1, EFCachingCommand.CacheHits);
            Assert.Equal(misses + 1, EFCachingCommand.CacheMisses);
            Assert.Equal(adds + 1, EFCachingCommand.CacheAdds);
            Assert.Equal(cacheable + 2, EFCachingCommand.CacheableCommands);
        }
    }
}
