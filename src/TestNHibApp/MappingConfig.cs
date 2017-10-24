using System;
using System.Collections.Generic;
using System.Text;
using NHibernate.Mapping.ByCode;

namespace TestNHibApp
{
    class MappingConfig
    {
        internal void Configure(ModelMapper mapper)
        {
            //Add a convention to include all 

            mapper.Class<Message>(mp =>
            {
                mp.Id(m => m.Id);
                mp.Property(m => m.DateAdded);
                mp.Property(m => m.Text);
            });
        }
    }
}
