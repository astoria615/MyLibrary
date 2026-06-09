using System;
using System.Configuration;

namespace MyLibrary.Models
{
    public partial class LibraryDataContext
    {
        public LibraryDataContext()
            : this(GetConnectionString())
        {
        }

        private static string GetConnectionString()
        {
            var connection = ConfigurationManager.ConnectionStrings["LibraryConnectionString"];

            if (connection == null)
            {
                throw new Exception("Connection string 'LibraryConnectionString' was not found in Web.config.");
            }

            return connection.ConnectionString;
        }
    }
}