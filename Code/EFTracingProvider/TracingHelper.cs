using System;
using System.Data.Objects;
using System.IO;
using EFProviderWrapperToolkit;

namespace EFTracingProvider
{
    public class TracingHelper<TEntities> where TEntities:ObjectContext
    {
        private TextWriter logOutput;
        private ObjectContext dbContext;

        private EFTracingConnection TracingConnection
        {
            get { return dbContext.UnwrapConnection<EFTracingConnection>(); }
        }

        public event EventHandler<CommandExecutionEventArgs> CommandExecuting
        {
            add => TracingConnection.CommandExecuting += value;
            remove { TracingConnection.CommandExecuting -= value; }
        }

        public event EventHandler<CommandExecutionEventArgs> CommandFinished
        {
            add { TracingConnection.CommandFinished += value; }
            remove { TracingConnection.CommandFinished -= value; }        }

        public event EventHandler<CommandExecutionEventArgs> CommandFailed
        {
            add { TracingConnection.CommandFailed += value; }
            remove { TracingConnection.CommandFailed -= value; }
        }

        private void AppendToLog(object sender, CommandExecutionEventArgs e)
        {
            if (logOutput != null)
            {
                logOutput.WriteLine(e.ToTraceString().TrimEnd());
                logOutput.WriteLine();
            }
        }

        public TextWriter Log
        {
            get { return logOutput; }
            set
            {
                if ((logOutput != null) != (value != null))
                {
                    if (value == null)
                    {
                        CommandExecuting -= AppendToLog;
                    }
                    else
                    {
                        CommandExecuting += AppendToLog;
                    }
                }

                logOutput = value;
            }
        }

        public TEntities CreateDbContextForTracing(string connectionString)
        {
            var func=new Func<string, TEntities>(name => (TEntities) Activator.CreateInstance(typeof(TEntities),
                EntityConnectionWrapperUtils.CreateEntityConnectionWithWrappersFromName(
                    name,
                    "EFTracingProvider"
                    //, "EFCachingProvider"
                )));
            return CreateDbContextForTracing(connectionString, func);
        }

        public TEntities CreateDbContextForTracing(string connectionString, Func<string, TEntities> func)
        {
            dbContext = func?.Invoke(connectionString);
            return (TEntities) dbContext;
        }
    }

}
