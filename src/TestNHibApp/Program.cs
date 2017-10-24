using FluentNHibernate.Automapping;
using FluentNHibernate.Cfg;
using FluentNHibernate.Cfg.Db;
using FluentNHibernate.Conventions;
using FluentNHibernate.Conventions.Helpers;
using FluentNHibernate.Conventions.Instances;
using NHibernate.Cfg;
using NHibernate.Dialect;
using NHibernate.Driver;
using NHibernate.Mapping.ByCode;
using NHibernate.Tool.hbm2ddl;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;

namespace TestNHibApp
{
    /// <summary>
    /// Our poco's all use this.
    /// </summary>
    public abstract class DataEntity
    {
        /// <summary>
        /// Natural keys be damned.
        /// </summary>
        public virtual Guid Id { get; protected internal set; } = Guid.NewGuid();
    }

    public class Message : DataEntity
    {
        public virtual Bottle Bottle { get; set; }
        public virtual DateTime DateAdded { get; set; }
        public virtual string Text { get; set; }
    }


    public class Bottle : DataEntity
    {
        /// <summary>
        /// Who originally opened this bottle.  Null if not yet opened.
        /// </summary>
        public virtual Person OpenedBy { get; set; }

        /// <summary>
        /// The messages in this bottle.  Sometimes people send more than one, you know!
        /// </summary>
        public virtual IList<Message> Messages { get; protected internal set; } = new List<Message>();
    }

    public class Island : DataEntity
    {
        public virtual string Name { get; set; }

        /// <summary>
        /// The castaways on this island. Navigation property only, the person owns this side.
        /// </summary>
        public virtual IList<Person> Castaways { get; protected internal set; } = new List<Person>();
    }

    public class Person : DataEntity
    {
        /// <summary>
        /// The island this person is stranded on.
        /// </summary>
        /// <remarks>
        /// A castaway can only be on one island.
        /// </remarks>
        public virtual Island Island { get; set; }

        /// <summary>
        /// The persons fav message.  Null if this is a soulless villian with no heartwarming preference.
        /// </summary>
        /// <remarks>
        /// This is a many to many.  A castaway doesn't have to open the bottle to love the message.
        /// </remarks>
        public virtual Message FavoriteMessage { get; set; }

        /// <summary>
        /// What bottles this castaway opened.
        /// </summary>
        /// <remarks>
        /// A bottle can only be opened by one castaway.
        /// </remarks>
        public virtual IList<Bottle> BottlesOpened { get; protected internal set; } = new List<Bottle>();
    }

    public class CascadeCollectionsConvention : IHasManyConvention
    {
        public void Apply(IOneToManyCollectionInstance instance)
        {
            instance.Cascade.All();
        }
    }

    public class CascadeManyToOneConvention : IReferenceConvention
    {
        public void Apply(FluentNHibernate.Conventions.Instances.IManyToOneInstance instance)
        {
            instance.Cascade.SaveUpdate();
        }
    }


    public class Program
    {
        static void Main(string[] args)
        {
            string connString = "Data Source=(localdb)\\MSSqlLocalDb;Initial Catalog=TestSimpleDb;Trusted_Connection=true;MultipleActiveResultSets=True";

            var sessionFactoryBuilder = Fluently.Configure()
               .Database(
                  MsSqlConfiguration
                     .MsSql2008
                     .ConnectionString(connString)
               )
               .Mappings(m =>
               {
                   var autoMap = AutoMap
                        .AssemblyOf<DataEntity>()
                        .IgnoreBase<DataEntity>()
                        .Where(n => n.IsSubclassOf(typeof(DataEntity)));

                   var assem = typeof(DataEntity).Assembly;

                   autoMap
                        .Conventions.AddAssembly(assem)
                        .UseOverridesFromAssembly(assem)
                        .Alterations(a => a.AddFromAssembly(assem));

                   //Ensure properties are mapped with [] around them, as the default sql mapper doesn't, which
                   //results in errors when mappingh properties like 'Key' or 'Group'
                   autoMap.Conventions.Add(ConventionBuilder.Property.Always(s =>
                          s.Column("[" + s.Property.Name + "]")
                      ));


                   autoMap.Override<Person>(map => map
                        .HasMany(p => p.BottlesOpened).KeyColumn("OpenedBy_id") .Inverse()
                   );

                   autoMap.Override<Bottle>(map => map
                        .HasMany(b => b.Messages).Cascade.AllDeleteOrphan()
                   );


                   m.AutoMappings.Add(autoMap);
               })
               .ExposeConfiguration(config =>
               {
                   //we'll auto create / update the db.  Very useful for getting started, very terrible for keeping going :)
                   CreateOrUpdateDatabase(connString, config);
               });

            //Get going.
            using (var sessionFac = sessionFactoryBuilder.BuildSessionFactory())
            {
                using (var session = sessionFac.OpenSession())
                {
                    //Do some stuff!
                    var tahiti = new Island
                    {
                        Name = "Tahiti"
                    };
                    session.Save(tahiti);
                    session.Flush();

                    var bob = new Person
                    {
                        Island = tahiti
                    };
                    session.Save(bob);
                    session.Flush();

                    var jackDaniels = new Bottle
                    {
                        OpenedBy = bob
                    };
                    session.Save(jackDaniels);
                    session.Flush();

                    jackDaniels.Messages.Add(new Message
                    {
                        Bottle = jackDaniels,
                        DateAdded = DateTime.Now.AddYears(-2),
                        Text = "Register now for an exciting opportunity!"
                    });
                    session.Save(jackDaniels.Messages[0]);
                    session.Flush();
                    jackDaniels.Messages.Add(new Message
                    {
                        Bottle = jackDaniels,
                        DateAdded = DateTime.Now.AddYears(-2),
                        Text = "Dear Bob.  How have you been?  When will you get home to feed me?? Sincerely -The Cat."
                    });
                    session.Save(jackDaniels.Messages[1]);
                    session.Flush();
                }

                using (var session = sessionFac.OpenStatelessSession())
                {
                    //Read message class to find hello world.
                    var messages = session.QueryOver<Message>().List();
                    foreach (var message in messages)
                    {
                        Console.WriteLine($"{message.DateAdded.ToString()}: {message.Text}");
                    }
                }
            }

            Console.WriteLine("All Done!");
            Console.ReadLine();
        }

        /// <summary>
        /// For a bit of fun, we'll have the database create itself.  In a production app, this would never be used, instead 
        /// something like fluent migrator would be used to create and manage the db schema.
        /// </summary>
        private static void CreateOrUpdateDatabase(string connString, Configuration config)
        {
            //Raw sql to create the initial catalog.
            var connStringBuilder = new SqlConnectionStringBuilder(connString);
            var catalog = connStringBuilder.InitialCatalog;
            var masterConnString = new SqlConnectionStringBuilder(connString);
            masterConnString.InitialCatalog = "master";
            using (SqlConnection conn = new SqlConnection(masterConnString.ConnectionString))
            using (SqlCommand cmd = conn.CreateCommand())
            {
                conn.Open();
                cmd.CommandText = $"IF NOT EXISTS (SELECT name FROM master.sys.databases WHERE name = N'{catalog}') CREATE DATABASE[{catalog}]";
                cmd.ExecuteNonQuery();
            }

            //Now use the nhib schema export to create/update it.
            var su = new SchemaUpdate(config);
            su.Execute(true, true);
        }
    }
}
