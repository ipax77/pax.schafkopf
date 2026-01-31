using Blazored.LocalStorage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace pax.schafkopf.Client.Services
{
    public static class StorageService
    {
        public static (string, string) GetUserName(ISyncLocalStorageService localStorage)
        {
            if (localStorage.ContainKey("name") && localStorage.ContainKey("auth"))
                return (localStorage.GetItemAsString("name"), localStorage.GetItemAsString("auth"));
            else
                return (null, null);
        }

        public static void SetUserName(ISyncLocalStorageService localStorage, string name, string auth)
        {
            localStorage.SetItem("name", name);
            localStorage.SetItem("auth", auth);
        }

        public static string GetTableID(ISyncLocalStorageService localStorage)
        {
            if (localStorage.ContainKey("tableid"))
                return localStorage.GetItemAsString("tableid");
            else
                return String.Empty;
        }

        public static void SetTableID(ISyncLocalStorageService localStorage, Guid tableid)
        {
            localStorage.SetItem("tableid", tableid.ToString());
        }
    }
}
