﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace CrmAdo.DdexProvider
{
    public static class GacHelper
    {

        public static string CurrentAssemblyDirectory
        {
            get
            {
                string codeBase = Assembly.GetExecutingAssembly().CodeBase;
                UriBuilder uri = new UriBuilder(codeBase);
                string path = Uri.UnescapeDataString(uri.Path);
                return Path.GetDirectoryName(path);
            }
        }

        public static bool EnsureRemovedFromGac(string assemblyName)
        {

            // var query = AssemblyCache.QueryAssemblyInfo(assemblyName);         
            AssemblyCacheUninstallDisposition disp;
            AssemblyCache.UninstallAssembly(assemblyName, null, out disp);
            switch (disp)
            {
                case AssemblyCacheUninstallDisposition.AlreadyUninstalled:
                case AssemblyCacheUninstallDisposition.ReferenceNotFound:
                case AssemblyCacheUninstallDisposition.Uninstalled:
                    return true;

                default:
                    return false;
            }
        }

        public static void AddToGac(string assemblyPath)
        {
            // var query = AssemblyCache.QueryAssemblyInfo(assemblyName);         
            //AssemblyCacheUninstallDisposition disp;
            //InstallReference installRef;
            //Guid refGuid = Guid.NewGuid();        
            AssemblyCache.InstallAssembly(assemblyPath, null, AssemblyCommitFlags.Default);

        }

    }
}
