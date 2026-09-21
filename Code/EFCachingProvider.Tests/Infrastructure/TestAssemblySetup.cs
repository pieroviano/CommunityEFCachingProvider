// Copyright (c) Microsoft Corporation.  All rights reserved.

using System;

namespace System.Runtime.CompilerServices
{
    // .NET Framework has no ModuleInitializerAttribute; the compiler only needs the type to exist.
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    internal sealed class ModuleInitializerAttribute : Attribute
    {
    }
}

namespace EFCachingProvider.Tests.Infrastructure
{
    internal static class TestAssemblySetup
    {
        /// <summary>
        /// DbProviderFactories copies its provider table the first time any factory is requested, so the
        /// wrapper providers must be registered before any test touches ADO.NET.
        /// </summary>
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void Initialize()
        {
            TestModel.RegisterProviders();
        }
    }
}
