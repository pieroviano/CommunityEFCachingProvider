// Copyright (c) Microsoft Corporation.  All rights reserved.

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using EFCachingProvider.Tests.Infrastructure;
using EFProviderWrapperToolkit;
using EFTracingProvider;
using Xunit;

namespace EFCachingProvider.Tests
{
    public class EFTracingCommandTests
    {
        private readonly FakeDbCommand store = new FakeDbCommand();
        private readonly EFTracingConnection connection = new EFTracingConnection(new FakeDbConnection());
        private readonly List<CommandExecutionEventArgs> events = new List<CommandExecutionEventArgs>();
        private readonly List<CommandExecutionStatus> statuses = new List<CommandExecutionStatus>();

        public EFTracingCommandTests()
        {
            this.connection.CommandExecuting += this.Record;
            this.connection.CommandFinished += this.Record;
            this.connection.CommandFailed += this.Record;
        }

        private void Record(object sender, CommandExecutionEventArgs e)
        {
            Assert.Same(this.connection, sender);
            this.events.Add(e);
            this.statuses.Add(e.Status);
        }

        private EFTracingCommand Command(string text = "SELECT 1")
        {
            var tree = TestModel.Query(TestModel.Customers);
            var command = new EFTracingCommand(this.store, new DbCommandDefinitionWrapper(null, tree, null));
            command.Connection = this.connection;
            command.CommandText = text;
            return command;
        }

        [Fact]
        public void ExecuteReader_RaisesExecutingThenFinished()
        {
            var command = this.Command();

            using (var reader = command.ExecuteReader())
            {
                Assert.Same(reader, this.events[1].Result);
            }

            Assert.Equal(new[] { CommandExecutionStatus.Executing, CommandExecutionStatus.Finished }, this.statuses);
            Assert.Equal("ExecuteReader", this.events[0].Method);
            Assert.Equal(command.CommandID, this.events[0].CommandId);
            Assert.Same(command.Definition.CommandTree, this.events[0].CommandTree);
        }

        [Fact]
        public void ExecuteNonQuery_ReportsTheResult()
        {
            Assert.Equal(1, this.Command().ExecuteNonQuery());

            Assert.Equal("ExecuteNonQuery", this.events[1].Method);
            Assert.Equal(1, this.events[1].Result);
            Assert.Equal(CommandExecutionStatus.Finished, this.events[1].Status);
        }

        [Fact]
        public void ExecuteScalar_ReportsTheResult()
        {
            this.store.ScalarResult = "x";

            Assert.Equal("x", this.Command().ExecuteScalar());
            Assert.Equal("ExecuteScalar", this.events[1].Method);
            Assert.Equal("x", this.events[1].Result);
        }

        [Theory]
        [InlineData("ExecuteReader")]
        [InlineData("ExecuteNonQuery")]
        [InlineData("ExecuteScalar")]
        public void Failure_RaisesFailedWithTheException_AndRethrows(string method)
        {
            var failure = new InvalidOperationException("boom");
            this.store.Failure = failure;
            var command = this.Command();

            var thrown = Assert.Throws<InvalidOperationException>(() =>
            {
                switch (method)
                {
                    case "ExecuteReader": command.ExecuteReader(); break;
                    case "ExecuteNonQuery": command.ExecuteNonQuery(); break;
                    default: command.ExecuteScalar(); break;
                }
            });

            Assert.Same(failure, thrown);
            Assert.Equal(new[] { CommandExecutionStatus.Executing, CommandExecutionStatus.Failed }, this.statuses);
            Assert.Same(failure, this.events[1].Result);
            Assert.Equal(method, this.events[1].Method);
        }

        [Fact]
        public void CommandIds_AreUnique()
        {
            Assert.NotEqual(this.Command().CommandID, this.Command().CommandID);
        }

        [Fact]
        public void ToTraceString_ListsTheParameters()
        {
            var command = this.Command("SELECT * FROM T WHERE A = @a AND B = @b AND C = @c");
            command.Parameters.Add(new SqlParameter("a", SqlDbType.NVarChar, 10) { Value = "x" });
            command.Parameters.Add(new SqlParameter("b", SqlDbType.Int) { Value = DBNull.Value });
            command.Parameters.Add(new SqlParameter("c", SqlDbType.Int) { Value = 5 });
            command.ExecuteNonQuery();

            string trace = this.events[0].ToTraceString();

            Assert.StartsWith("SELECT * FROM T WHERE A = @a AND B = @b AND C = @c", trace);
            Assert.Contains("-- a (dbtype=String, size=10, direction=Input) = \"x\"", trace);
            Assert.Contains("-- b (dbtype=Int32, size=0, direction=Input) = null", trace);
            Assert.Contains("-- c (dbtype=Int32, size=0, direction=Input) = 5", trace);
        }

        [Fact]
        public void SetTraceOutput_WritesEachCommand()
        {
            var output = new StringWriter();
            this.connection.SetTraceOutput(output);

            this.Command("SELECT 42").ExecuteNonQuery();

            Assert.Contains("SELECT 42", output.ToString());
        }

        [Fact]
        public void GetTracingConnection_FindsTheWrapperInTheChain()
        {
            Assert.Same(this.connection, this.connection.GetTracingConnection());
            Assert.Throws<ArgumentNullException>(() => ((System.Data.Common.DbConnection)null).GetTracingConnection());
        }

        [Fact]
        public void LogAction_ReceivesEveryEvent()
        {
            var logged = new List<CommandExecutionStatus>();
            Action<CommandExecutionEventArgs> previous = EFTracingProviderConfiguration.LogAction;
            EFTracingProviderConfiguration.LogAction = e => logged.Add(e.Status);
            try
            {
                var command = new EFTracingCommand(this.store, new DbCommandDefinitionWrapper(null, TestModel.Query(TestModel.Customers), null));
                command.Connection = new EFTracingConnection(new FakeDbConnection());
                command.ExecuteNonQuery();
            }
            finally
            {
                EFTracingProviderConfiguration.LogAction = previous;
            }

            Assert.Equal(new[] { CommandExecutionStatus.Executing, CommandExecutionStatus.Finished }, logged);
        }

        [Fact]
        public void LogToFile_AppendsEachCommand()
        {
            string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".log");
            string previous = EFTracingProviderConfiguration.LogToFile;
            EFTracingProviderConfiguration.LogToFile = file;
            try
            {
                var command = new EFTracingCommand(this.store, new DbCommandDefinitionWrapper(null, TestModel.Query(TestModel.Customers), null));
                command.Connection = new EFTracingConnection(new FakeDbConnection());
                command.CommandText = "SELECT 7";
                command.ExecuteNonQuery();

                Assert.Contains("SELECT 7", File.ReadAllText(file));
            }
            finally
            {
                EFTracingProviderConfiguration.LogToFile = previous;
                File.Delete(file);
            }
        }
    }
}
