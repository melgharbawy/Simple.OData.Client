﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Simple.OData.Client.Extensions;

namespace Simple.OData.Client
{
    class Schema : ISchema
    {
        private static readonly SimpleDictionary<string, ISchema> Instances = new SimpleDictionary<string, ISchema>();

        private readonly Model _model;

        private readonly SchemaProvider _schemaProvider;
        private readonly Func<Task<string>> _resolveMetadataAsync;
        private Func<EdmSchema> _createEdmSchema;
        private Func<ProviderMetadata> _createProviderMetadata;
        private string _metadataString;

        private Lazy<EdmSchema> _lazyMetadata;
        private Lazy<ProviderMetadata> _lazyProviderMetadata;
        private Lazy<EntitySetCollection> _lazyTables;
        private Lazy<List<EdmEntityType>> _lazyEntityTypes;
        private Lazy<List<EdmComplexType>> _lazyComplexTypes;

        private Schema(string metadataString, Func<Task<string>> resolveMedatataAsync)
        {
            ResetCache();
            _model = new Model(this);

            _metadataString = metadataString;
            _resolveMetadataAsync = resolveMedatataAsync;

            if (_resolveMetadataAsync == null)
            {
                _createEdmSchema = () => ResponseReader.GetSchema(_metadataString);
            }
        }

        private Schema(SchemaProvider schemaProvider)
        {
            ResetCache();
            _model = new Model(this);

            _schemaProvider = schemaProvider;
        }

        internal void ResetCache()
        {
            _metadataString = null;

            _lazyMetadata = new Lazy<EdmSchema>(CreateEdmSchema);
            _lazyProviderMetadata = new Lazy<ProviderMetadata>(CreateProviderMetadata);
            _lazyTables = new Lazy<EntitySetCollection>(CreateTableCollection);
            _lazyEntityTypes = new Lazy<List<EdmEntityType>>(CreateEntityTypeCollection);
            _lazyComplexTypes = new Lazy<List<EdmComplexType>>(CreateComplexTypeCollection);
        }

        internal Model Model
        {
            get { return _model; }
        }

        public async Task<ISchema> ResolveAsync(CancellationToken cancellationToken)
        {
            if (_metadataString == null)
            {
                if (_schemaProvider != null)
                {
                    var response = await _schemaProvider.SendSchemaRequestAsync(cancellationToken);
                    _metadataString = await _schemaProvider.GetSchemaAsStringAsync(response);
                    var providerMetadata = await _schemaProvider.GetMetadataAsync(response);
                    _createProviderMetadata = () => providerMetadata;
                    var metadata = await _schemaProvider.GetSchemaAsync(providerMetadata);
                    _createEdmSchema = () => metadata;
                }
                else
                {
                    _metadataString = await _resolveMetadataAsync();
                    _createEdmSchema = () => ResponseReader.GetSchema(_metadataString);
                    // TODO
                }
            }

            _lazyMetadata = new Lazy<EdmSchema>(CreateEdmSchema);
            _lazyProviderMetadata = new Lazy<ProviderMetadata>(CreateProviderMetadata);
            return this;
        }

        public EdmSchema Metadata
        {
            get { return _lazyMetadata.Value; }
        }

        public ProviderMetadata ProviderMetadata
        {
            get { return _lazyProviderMetadata.Value; }
        }

        public string MetadataAsString
        {
            get { return _metadataString; }
        }

        public string TypesNamespace
        {
            get { return string.Empty; }
        }

        public string ContainersNamespace
        {
            get { return string.Empty; }
        }

        public IEnumerable<EntitySet> EntitySets
        {
            get { return _lazyTables.Value.AsEnumerable(); }
        }

        public bool HasTable(string entitySetName)
        {
            return _lazyTables.Value.Contains(entitySetName);
        }

        public EntitySet FindEntitySet(string entitySetName)
        {
            return _lazyTables.Value.Find(entitySetName);
        }

        public EntitySet FindBaseEntitySet(string entitySetPath)
        {
            return this.FindEntitySet(entitySetPath.Split('/').First());
        }

        public EntitySet FindConcreteEntitySet(string entitySetPath)
        {
            var items = entitySetPath.Split('/');
            if (items.Count() > 1)
            {
                var baseTable = this.FindEntitySet(items[0]);
                var table = string.IsNullOrEmpty(items[1])
                    ? baseTable
                    : baseTable.FindDerivedTable(items[1]);
                return table;
            }
            else
            {
                return this.FindEntitySet(entitySetPath);
            }
        }

        public IEnumerable<EdmEntityType> EntityTypes
        {
            get { return _lazyEntityTypes.Value.AsEnumerable(); }
        }

        public IEnumerable<EdmComplexType> ComplexTypes
        {
            get { return _lazyComplexTypes.Value.AsEnumerable(); }
        }

        private EdmSchema CreateEdmSchema()
        {
            return _createEdmSchema();
        }

        private ProviderMetadata CreateProviderMetadata()
        {
            return _createProviderMetadata();
        }

        private EntitySetCollection CreateTableCollection()
        {
            return new EntitySetCollection(_model.GetTables()
                .Select(table => new EntitySet(table.ActualName, table.EntityType, null, this)));
        }

        private List<EdmEntityType> CreateEntityTypeCollection()
        {
            return new List<EdmEntityType>(_model.GetEntityTypes());
        }

        private List<EdmComplexType> CreateComplexTypeCollection()
        {
            return new List<EdmComplexType>(_model.GetComplexTypes());
        }

        internal static ISchema FromUrl(string urlBase, ICredentials credentials = null)
        {
            return Instances.GetOrAdd(urlBase, new Schema(new SchemaProvider(urlBase, credentials)));
        }

        internal static ISchema FromMetadata(string metadataString)
        {
            return new Schema(metadataString, null);
        }

        internal static void Add(string urlBase, ISchema schema)
        {
            Instances.GetOrAdd(urlBase, sp => schema);
        }

        internal static void ClearCache()
        {
            Instances.Clear();
        }
    }
}
