// Copyright (c) Microsoft Corporation.  All rights reserved.

using System;
using System.Data;
using EFCachingProvider.Caching;

namespace EFCachingProvider
{
    /// <summary>
    /// Implementation of <see cref="DbDataReader"/> which returns results from <see cref="DbQueryResults"/> object.
    /// </summary>
    internal class CachingDataReaderCacheReader : EFCachingDataReaderBase
    {
        private DbQueryResults queryResults;
        private int currentRow;
        private bool isClosed;

        /// <summary>
        /// Initializes a new instance of the CachingDataReaderCacheReader class.
        /// </summary>
        /// <param name="queryResults">The query results.</param>
        public CachingDataReaderCacheReader(DbQueryResults queryResults)
        {
            this.queryResults = queryResults;
        }

        /// <summary>
        /// Gets a value that indicates whether this <see cref="T:System.Data.Common.DbDataReader"/> contains one or more rows.
        /// </summary>
        /// <value></value>
        /// <returns>true if the <see cref="T:System.Data.Common.DbDataReader"/> contains one or more rows; otherwise false.
        /// </returns>
        public override bool HasRows
        {
            get { return this.queryResults.Rows.Count > 0; }
        }

        /// <summary>
        /// Gets a value indicating whether the <see cref="T:System.Data.Common.DbDataReader"/> is closed.
        /// </summary>
        /// <value></value>
        /// <returns>true if the <see cref="T:System.Data.Common.DbDataReader"/> is closed; otherwise false.
        /// </returns>
        public override bool IsClosed
        {
            get { return this.isClosed; }
        }

        /// <summary>
        /// Gets a value indicating the depth of nesting for the current row.
        /// </summary>
        /// <value></value>
        /// <returns>
        /// The depth of nesting for the current row.
        /// </returns>
        public override int Depth
        {
            get { throw new NotImplementedException(); }
        }

        /// <summary>
        /// Gets the number of rows changed, inserted, or deleted by execution of the SQL statement.
        /// </summary>
        /// <value></value>
        /// <returns>
        /// The number of rows changed, inserted, or deleted. -1 for SELECT statements; 0 if no rows were affected or the statement failed.
        /// </returns>
        public override int RecordsAffected
        {
            get { throw new NotImplementedException(); }
        }

        /// <summary>
        /// Gets name of the data type of the specified column.
        /// </summary>
        /// <param name="ordinal">The zero-based column ordinal.</param>
        /// <returns>
        /// A string representing the name of the data type.
        /// </returns>
        public override string GetDataTypeName(int ordinal)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Gets the data type of the specified column.
        /// </summary>
        /// <param name="ordinal">The zero-based column ordinal.</param>
        /// <returns>The data type of the specified column.</returns>
        public override Type GetFieldType(int ordinal)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Gets the name of the column, given the zero-based column ordinal.
        /// </summary>
        /// <param name="ordinal">The zero-based column ordinal.</param>
        /// <returns>The name of the specified column.</returns>
        public override string GetName(int ordinal)
        {
            return this.queryResults.ColumnNames[ordinal];
        }

        /// <summary>
        /// Gets the column ordinal given the name of the column.
        /// </summary>
        /// <param name="name">The name of the column.</param>
        /// <returns>The zero-based column ordinal.</returns>
        /// <exception cref="T:System.IndexOutOfRangeException">
        /// The name specified is not a valid column name.
        /// </exception>
        public override int GetOrdinal(string name)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Returns a <see cref="T:System.Data.DataTable"/> that describes the column metadata of the <see cref="T:System.Data.Common.DbDataReader"/>.
        /// </summary>
        /// <returns>
        /// A <see cref="T:System.Data.DataTable"/> that describes the column metadata.
        /// </returns>
        public override DataTable GetSchemaTable()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Advances the reader to the next result when reading the results of a batch of statements.
        /// </summary>
        /// <returns>
        /// true if there are more result sets; otherwise false.
        /// </returns>
        public override bool NextResult()
        {
            return false;
        }

        /// <summary>
        /// Closes the <see cref="T:System.Data.Common.DbDataReader"/> object.
        /// </summary>
        public override void Close()
        {
            this.isClosed = true;
        }

        /// <summary>
        /// Advances the reader to the next record in a result set.
        /// </summary>
        /// <returns>
        /// true if there are more rows; otherwise false.
        /// </returns>
        public override bool Read()
        {
            if (this.currentRow < this.queryResults.Rows.Count)
            {
                object[] values = this.queryResults.Rows[this.currentRow++];
                SetValues(values);
                return true;
            }
            else
            {
                return false;
            }
        }
    }
}
