// Copyright (c) Microsoft Corporation.  All rights reserved.

using System;
using System.Collections.Generic;
using System.Data;
using EFCachingProvider.Caching;
using EFCachingProvider.Tests.Infrastructure;
using Xunit;

namespace EFCachingProvider.Tests
{
    public class CachingDataReaderTests
    {
        private static DbQueryResults Results(string columns, params object[][] rows)
        {
            var results = new DbQueryResults();
            foreach (string c in columns.Split(','))
            {
                results.ColumnNames.Add(c);
            }

            foreach (object[] row in rows)
            {
                results.Rows.Add(row);
            }

            return results;
        }

        private static EFCachingDataReaderCacheWriter Writer(DataTable table, int maxRows, List<DbQueryResults> added)
        {
            return new EFCachingDataReaderCacheWriter(table.CreateDataReader(), maxRows, added.Add);
        }

        [Fact]
        public void CacheReader_ReturnsRowsInOrder()
        {
            var reader = new CachingDataReaderCacheReader(Results("Id,Name", new object[] { 1, "a" }, new object[] { 2, "b" }));

            Assert.True(reader.HasRows);
            Assert.True(reader.Read());
            Assert.Equal(1, reader.GetInt32(0));
            Assert.Equal("a", reader.GetString(1));
            Assert.True(reader.Read());
            Assert.Equal(2, reader[0]);
            Assert.False(reader.Read());
            Assert.False(reader.NextResult());
        }

        [Fact]
        public void CacheReader_Empty_HasNoRows()
        {
            var reader = new CachingDataReaderCacheReader(Results("Id"));

            Assert.False(reader.HasRows);
            Assert.False(reader.Read());
        }

        [Fact]
        public void CacheReader_FieldCount_IsKnownBeforeTheFirstRead()
        {
            var reader = new CachingDataReaderCacheReader(Results("Id,Name"));

            Assert.Equal(2, reader.FieldCount);
        }

        [Fact]
        public void CacheReader_GetName_And_GetOrdinal_AgreeWithTheColumns()
        {
            var reader = new CachingDataReaderCacheReader(Results("Id,Name", new object[] { 1, "a" }));

            Assert.Equal("Name", reader.GetName(1));
            Assert.Equal(1, reader.GetOrdinal("Name"));
            Assert.Equal(1, reader.GetOrdinal("NAME"));
            Assert.True(reader.Read());
            Assert.Equal("a", reader["Name"]);
        }

        [Fact]
        public void CacheReader_GetOrdinal_UnknownColumn_ThrowsIndexOutOfRange()
        {
            var reader = new CachingDataReaderCacheReader(Results("Id"));

            Assert.Throws<IndexOutOfRangeException>(() => reader.GetOrdinal("Nope"));
        }

        [Fact]
        public void CacheReader_DepthAndRecordsAffected_FollowTheDataReaderContract()
        {
            // A cached result is always a top-level SELECT: depth 0, and -1 records affected.
            var reader = new CachingDataReaderCacheReader(Results("Id"));

            Assert.Equal(0, reader.Depth);
            Assert.Equal(-1, reader.RecordsAffected);
        }

        [Fact]
        public void CacheReader_Close_MarksTheReaderClosed()
        {
            var reader = new CachingDataReaderCacheReader(Results("Id"));
            Assert.False(reader.IsClosed);

            reader.Close();

            Assert.True(reader.IsClosed);
        }

        [Fact]
        public void CacheReader_IsDBNull_DetectsDbNull()
        {
            var reader = new CachingDataReaderCacheReader(Results("A,B", new object[] { DBNull.Value, 1 }));
            reader.Read();

            Assert.True(reader.IsDBNull(0));
            Assert.False(reader.IsDBNull(1));
        }

        [Fact]
        public void GetValues_CopiesAtMostTheBufferLength()
        {
            var reader = new CachingDataReaderCacheReader(Results("A,B,C", new object[] { 1, 2, 3 }));
            reader.Read();

            var shorter = new object[2];
            Assert.Equal(2, reader.GetValues(shorter));
            Assert.Equal(new object[] { 1, 2 }, shorter);

            var longer = new object[4];
            Assert.Equal(3, reader.GetValues(longer));
            Assert.Equal(new object[] { 1, 2, 3, null }, longer);
        }

        [Fact]
        public void GetBytes_CopiesTheRequestedSlice()
        {
            var reader = new CachingDataReaderCacheReader(Results("Blob", new object[] { new byte[] { 1, 2, 3, 4, 5 } }));
            reader.Read();

            var buffer = new byte[4];
            long copied = reader.GetBytes(0, 1, buffer, 1, 3);

            Assert.Equal(3, copied);
            Assert.Equal(new byte[] { 0, 2, 3, 4 }, buffer);
        }

        [Fact]
        public void GetBytes_NullBuffer_ReturnsTheLength()
        {
            var reader = new CachingDataReaderCacheReader(Results("Blob", new object[] { new byte[] { 1, 2, 3 } }));
            reader.Read();

            Assert.Equal(3, reader.GetBytes(0, 0, null, 0, 0));
        }

        [Fact]
        public void GetBytes_PastTheEnd_CopiesNothing()
        {
            var reader = new CachingDataReaderCacheReader(Results("Blob", new object[] { new byte[] { 1, 2, 3 } }));
            reader.Read();

            Assert.Equal(0, reader.GetBytes(0, 3, new byte[2], 0, 2));
            Assert.Equal(1, reader.GetBytes(0, 2, new byte[2], 0, 2));
        }

        [Fact]
        public void GetChars_CopiesTheRequestedSlice()
        {
            var reader = new CachingDataReaderCacheReader(Results("Text", new object[] { "hello" }));
            reader.Read();

            var buffer = new char[3];
            Assert.Equal(3, reader.GetChars(0, 1, buffer, 0, 3));
            Assert.Equal("ell", new string(buffer));
            Assert.Equal(5, reader.GetChars(0, 0, null, 0, 0));
        }

        [Fact]
        public void TypedGetters_ReturnTheStoredValues()
        {
            var guid = Guid.NewGuid();
            var date = new DateTime(2020, 2, 3);
            var reader = new CachingDataReaderCacheReader(Results(
                "b,y,c,d,m,f,g,s,l,r",
                new object[] { true, (byte)7, 'x', date, 1.5m, 2.5f, guid, (short)3, 4L, 5.5d }));
            reader.Read();

            Assert.True(reader.GetBoolean(0));
            Assert.Equal((byte)7, reader.GetByte(1));
            Assert.Equal('x', reader.GetChar(2));
            Assert.Equal(date, reader.GetDateTime(3));
            Assert.Equal(1.5m, reader.GetDecimal(4));
            Assert.Equal(2.5f, reader.GetFloat(5));
            Assert.Equal(guid, reader.GetGuid(6));
            Assert.Equal((short)3, reader.GetInt16(7));
            Assert.Equal(4L, reader.GetInt64(8));
            Assert.Equal(5.5d, reader.GetDouble(9));
        }

        [Fact]
        public void Writer_PassesRowsThrough_AndCachesThemOnClose()
        {
            var added = new List<DbQueryResults>();
            var writer = Writer(FakeDbCommand.Table("Id,Name", new object[] { 1, "a" }, new object[] { 2, "b" }), 10, added);

            Assert.Equal(2, writer.FieldCount);
            Assert.Equal("Name", writer.GetName(1));
            Assert.Equal(1, writer.GetOrdinal("Name"));
            Assert.True(writer.Read());
            Assert.Equal("a", writer["Name"]);
            Assert.True(writer.Read());
            Assert.False(writer.Read());
            writer.Close();

            var entry = Assert.Single(added);
            Assert.Equal(new[] { "Id", "Name" }, entry.ColumnNames);
            Assert.Equal(2, entry.Rows.Count);
            Assert.Equal(new object[] { 2, "b" }, entry.Rows[1]);
            Assert.True(writer.IsClosed);
        }

        [Fact]
        public void Writer_ClosedBeforeTheEnd_CachesTheCompleteResult()
        {
            // A consumer that stops early (First(), a broken foreach) must not leave a truncated result in
            // the cache: the next execution would silently return fewer rows than the store holds.
            var added = new List<DbQueryResults>();
            var writer = Writer(FakeDbCommand.Table("Id", new object[] { 1 }, new object[] { 2 }, new object[] { 3 }), 10, added);

            Assert.True(writer.Read());
            writer.Close();

            var entry = Assert.Single(added);
            Assert.Equal(3, entry.Rows.Count);
        }

        [Fact]
        public void Writer_ClosedBeforeTheEnd_WithMoreRowsThanCacheable_CachesNothing()
        {
            var added = new List<DbQueryResults>();
            var writer = Writer(FakeDbCommand.Table("Id", new object[] { 1 }, new object[] { 2 }, new object[] { 3 }), 2, added);

            Assert.True(writer.Read());
            writer.Close();

            Assert.Empty(added);
        }

        [Fact]
        public void Writer_MoreRowsThanMax_CachesNothing()
        {
            var added = new List<DbQueryResults>();
            var writer = Writer(FakeDbCommand.Table("Id", new object[] { 1 }, new object[] { 2 }, new object[] { 3 }), 2, added);

            while (writer.Read())
            {
            }

            writer.Close();

            Assert.Empty(added);
        }

        [Fact]
        public void Writer_ExactlyMaxRows_IsCached()
        {
            var added = new List<DbQueryResults>();
            var writer = Writer(FakeDbCommand.Table("Id", new object[] { 1 }, new object[] { 2 }), 2, added);

            while (writer.Read())
            {
            }

            writer.Close();

            Assert.Equal(2, Assert.Single(added).Rows.Count);
        }

        [Fact]
        public void Writer_ClosedTwice_CachesOnce()
        {
            var added = new List<DbQueryResults>();
            var writer = Writer(FakeDbCommand.Table("Id", new object[] { 1 }), 10, added);
            while (writer.Read())
            {
            }

            writer.Close();
            writer.Close();
            writer.Dispose();

            Assert.Single(added);
        }

        [Fact]
        public void Writer_EmptyResult_IsCached()
        {
            var added = new List<DbQueryResults>();
            var writer = Writer(FakeDbCommand.Table("Id"), 10, added);

            Assert.False(writer.HasRows);
            Assert.False(writer.Read());
            writer.Close();

            Assert.Empty(Assert.Single(added).Rows);
        }

        [Fact]
        public void Writer_ExposesTheWrappedSchema()
        {
            var table = FakeDbCommand.Table("Id");
            var writer = Writer(table, 10, new List<DbQueryResults>());

            Assert.Equal(typeof(object), writer.GetFieldType(0));
            Assert.Equal(0, writer.Depth);
            Assert.NotNull(writer.GetSchemaTable());
        }
    }
}
