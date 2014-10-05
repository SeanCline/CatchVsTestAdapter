using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace CatchVsTestAdapter
{
    [PackageRegistration(UseManagedResourcesOnly = true)]
    [Guid(@"a6bfe5f0-69bf-4bb4-8bb7-6aa58d7f6888")]
    public sealed class CatchVsTestAdapterPackage : Package
    {
    }
}
