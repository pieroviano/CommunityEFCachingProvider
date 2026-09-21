// Copyright (c) Microsoft Corporation.  All rights reserved.

using System;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;

namespace EFCachingProvider.Tests.Infrastructure
{
    /// <summary>In-memory stand-in for the wrapped store connection. Only the wrappers are under test.</summary>
    internal sealed class FakeDbConnection : DbConnection
    {
        private ConnectionState state = ConnectionState.Closed;

        public int TransactionsStarted { get; private set; }

        public System.Transactions.Transaction EnlistedTransaction { get; private set; }

        public override string ConnectionString { get; set; } = "fake";

        public override string Database
        {
            get { return "fake"; }
        }

        public override string DataSource
        {
            get { return "fake"; }
        }

        public override string ServerVersion
        {
            get { return "1.0"; }
        }

        public override ConnectionState State
        {
            get { return this.state; }
        }

        public override void ChangeDatabase(string databaseName)
        {
        }

        public override void Close()
        {
            this.state = ConnectionState.Closed;
        }

        public override void Open()
        {
            this.state = ConnectionState.Open;
        }

        public override void EnlistTransaction(System.Transactions.Transaction transaction)
        {
            this.EnlistedTransaction = transaction;
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            this.TransactionsStarted++;
            return new FakeDbTransaction(this, isolationLevel);
        }

        protected override DbCommand CreateDbCommand()
        {
            return new FakeDbCommand { Connection = this };
        }
    }

    internal sealed class FakeDbTransaction : DbTransaction
    {
        private readonly DbConnection connection;
        private readonly IsolationLevel isolationLevel;

        public FakeDbTransaction(DbConnection connection, IsolationLevel isolationLevel)
        {
            this.connection = connection;
            this.isolationLevel = isolationLevel;
        }

        public bool Committed { get; private set; }

        public bool RolledBack { get; private set; }

        public override IsolationLevel IsolationLevel
        {
            get { return this.isolationLevel; }
        }

        protected override DbConnection DbConnection
        {
            get { return this.connection; }
        }

        public override void Commit()
        {
            this.Committed = true;
        }

        public override void Rollback()
        {
            this.RolledBack = true;
        }
    }

    /// <summary>A store command that returns <see cref="Result"/> and counts how often it really ran.</summary>
    internal sealed class FakeDbCommand : DbCommand
    {
        private readonly DbParameterCollection parameters = new SqlCommand().Parameters;

        public FakeDbCommand()
        {
            this.Result = Table("Id");
        }

        public DataTable Result { get; set; }

        public Exception Failure { get; set; }

        public object ScalarResult { get; set; }

        public int ReaderExecutions { get; private set; }

        public int NonQueryExecutions { get; private set; }

        public override string CommandText { get; set; }

        public override int CommandTimeout { get; set; }

        public override CommandType CommandType { get; set; } = CommandType.Text;

        public override bool DesignTimeVisible { get; set; }

        public override UpdateRowSource UpdatedRowSource { get; set; }

        protected override DbConnection DbConnection { get; set; }

        protected override DbParameterCollection DbParameterCollection
        {
            get { return this.parameters; }
        }

        protected override DbTransaction DbTransaction { get; set; }

        /// <summary>Builds a table with the given columns and rows.</summary>
        public static DataTable Table(string columns, params object[][] rows)
        {
            var table = new DataTable();
            foreach (string column in columns.Split(','))
            {
                table.Columns.Add(column.Trim(), typeof(object));
            }

            foreach (object[] row in rows)
            {
                table.Rows.Add(row);
            }

            return table;
        }

        public override void Cancel()
        {
        }

        public override int ExecuteNonQuery()
        {
            this.NonQueryExecutions++;
            this.ThrowIfFailing();
            return 1;
        }

        public override object ExecuteScalar()
        {
            this.ThrowIfFailing();
            return this.ScalarResult;
        }

        public override void Prepare()
        {
        }

        protected override DbParameter CreateDbParameter()
        {
            return new SqlParameter();
        }

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            this.ReaderExecutions++;
            this.ThrowIfFailing();
            return this.Result.CreateDataReader();
        }

        private void ThrowIfFailing()
        {
            if (this.Failure != null)
            {
                throw this.Failure;
            }
        }
    }
}
