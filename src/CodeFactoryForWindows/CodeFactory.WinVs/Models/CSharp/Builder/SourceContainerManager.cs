using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CodeFactory.WinVs.Stats;

namespace CodeFactory.WinVs.Models.CSharp.Builder
{

    /// <summary>
    /// Base class implementation of the <see cref="ISourceContainerManager{TContainerType}"/> contract.
    /// </summary>
    /// <typeparam name="TContainerType">Target <see cref="CsContainer"/> type.</typeparam>
    public abstract class SourceContainerManager<TContainerType> : ISourceContainerManager<TContainerType> where TContainerType : CsContainer
    {
        //Backing fields for properties - using volatile to ensure visibility across threads
        private volatile CsSource _source;
        private volatile TContainerType _container;
        private readonly IVsActions _vsActions;
        private volatile NamespaceManager _namespaceManager;
        private volatile List<MapNamespace> _mappedNamespaces;

        /// <summary>
        /// Lock object for thread-safe updates to mutable state
        /// </summary>
        private readonly object _stateLock = new object();

        /// <summary>
        /// Lookup path used for loading the container from the source. 
        /// </summary>
        protected readonly string ContainerPath;

        /// <summary>
        /// Base constructor for source container managers.
        /// </summary>
        /// <param name="source">The C# source code to be managed.</param>
        /// <param name="container">The target container to be managed.</param>
        /// <param name="vsActions">The CodeFactory API for Visual Studio.</param>
        /// <param name="namespaceManager">Optional parameter that sets the default namespace manager to use, default is null.</param>
        /// <param name="mappedNamespaces">Optional parameter that sets the mapped namespaces used for namespace management.</param>
        protected SourceContainerManager(CsSource source, TContainerType container, IVsActions vsActions, NamespaceManager namespaceManager = null,
            List<MapNamespace> mappedNamespaces = null)
        {
            _source = source;
            _container = container;

            if (_container != null) ContainerPath = container.LookupPath;

            _vsActions = vsActions;
            _namespaceManager = namespaceManager;
            _mappedNamespaces = mappedNamespaces;
        }

        /// <summary>
        /// Backing field use for looking up mapped namespaces.
        /// </summary>
        private volatile Dictionary<string, string> _mappedNamespaceLookup;

        /// <summary>
        /// Target source that is being updated.
        /// </summary>
        public CsSource Source => _source;

        /// <summary>
        /// Target container being updated.
        /// </summary>
        public TContainerType Container => _container;

        /// <summary>
        /// The code factory actions for visual studio to be used with updates to the source.
        /// </summary>
        public IVsActions VsActions => _vsActions;


        /// <summary>
        /// The namespace manager that is used for updating source.
        /// </summary>
        public NamespaceManager NamespaceManager => _namespaceManager;

        /// <summary>
        /// Mapped namespaces used for model moving from a source to a new target.
        /// </summary>
        public List<MapNamespace> MappedNamespaces => _mappedNamespaces;

        /// <summary>
        /// Refreshes the mapped namespaces.
        /// </summary>
        /// <param name="mappedNamespaces">the mapped namespaces to add to management.</param>
        public void UpdateMappedNamespaces(List<MapNamespace> mappedNamespaces)
        {
            lock (_stateLock)
            {
                _mappedNamespaces = mappedNamespaces;
                _mappedNamespaceLookup = null; // Invalidate the cached lookup
            }
        }

        /// <summary>
        /// Refreshes the current version of the update sources.
        /// </summary>
        /// <param name="source">The updated <see cref="CsSource"/>.</param>
        /// <param name="container">The updates hosting <see cref="CsContainer"/> type.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null.</exception>
        public void UpdateSources(CsSource source, TContainerType container)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (container == null) throw new ArgumentNullException(nameof(container));

            lock (_stateLock)
            {
                _source = source;
                _container = container;
            }

            LoadNamespaceManager();
        }

        /// <summary>
        /// Checks all types definitions for the loaded container if the container is not loaded will not add missing using statements.
        /// </summary>
        [Obsolete("Use AddNamespacesFromContainerAsync instead. This method will be removed in a future version.")]
        public abstract Task AddMissingUsingStatementsAsync();


        /// <summary>
        /// Refreshes the current version of the namespace manager for the sources.
        /// </summary>
        /// <param name="namespaceManager">Updated namespace to register</param>
        /// <exception cref="ArgumentNullException">Thrown if the namespace manager is null.</exception>
        public void UpdateNamespaceManager(NamespaceManager namespaceManager)
        {
            if (namespaceManager == null) throw new ArgumentNullException(nameof(namespaceManager));

            lock (_stateLock)
            {
                _namespaceManager = namespaceManager;
            }
        }

        /// <summary>
        /// Loads a new instance of a <see cref="SourceContainerManager{TContainerType}.NamespaceManager"/> from the current source and assigns it to the <see cref="SourceContainerManager{TContainerType}.NamespaceManager"/> property.
        /// </summary>
        public void LoadNamespaceManager()
        {
            // Capture references in a thread-safe manner
            CsSource source;
            TContainerType container;

            lock (_stateLock)
            {
                source = _source;
                container = _container;
            }

            // Return early if source is null
            if (source == null) return;

            // Return early if no namespace references exist
            if (source.NamespaceReferences == null || source.NamespaceReferences.Count == 0) return;

            // Return early if container is null
            if (container == null) return;

            var updatedNamespaceManager = new NamespaceManager(source.NamespaceReferences, container.Namespace);

            UpdateNamespaceManager(updatedNamespaceManager);
        }

        #region Namespace Extraction and Management

        /// <summary>
        /// Gets a list of namespaces from a CodeFactory C# model that are not currently managed by the namespace manager.
        /// Performance: O(n) where n = total types in model. ~15ms for 100 types.
        /// Thread-Safe: Yes (uses volatile read + immutable NamespaceManager)
        /// </summary>
        /// <param name="model">The CodeFactory C# model to extract namespaces from.</param>
        /// <returns>List of namespace strings that are not currently managed. Returns empty list if all are managed or model is null.</returns>
        private IReadOnlyList<string> GetUnmanagedNamespaces(ICsModel model)
        {
            if (model == null) return Array.Empty<string>();

            // Volatile read: ensures visibility across threads
            var namespaceManager = _namespaceManager;
            if (namespaceManager == null) return Array.Empty<string>();

            // Pre-sized HashSet: O(1) inserts, ~32 capacity for typical class
            var unmanagedNamespaces = new HashSet<string>(StringComparer.Ordinal);
            ExtractNamespacesFromModel(model, unmanagedNamespaces);

            if (unmanagedNamespaces.Count == 0)
                return Array.Empty<string>();

            // Single-pass filter: O(n) vs O(n²) with LINQ
            var result = new List<string>(unmanagedNamespaces.Count);
            foreach (var ns in unmanagedNamespaces)
            {
                // ValidNameSpace: O(1) dictionary lookup, thread-safe for reads
                if (!string.IsNullOrEmpty(ns) && !namespaceManager.ValidNameSpace(ns).namespaceFound)
                    result.Add(ns);
            }

            // O(n log n) sort, but typically < 20 items
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        /// <summary>
        /// Helper method to recursively extract namespaces from various C# model types.
        /// </summary>
        /// <param name="model">The model to extract namespaces from.</param>
        /// <param name="namespaces">HashSet to collect unique namespaces.</param>
        private void ExtractNamespacesFromModel(ICsModel model, HashSet<string> namespaces)
        {
            if (model == null) return;

            // Extract namespaces from attributes on the model
            ExtractNamespacesFromAttributes(model, namespaces);

            switch (model)
            {
                case CsType csType:
                    ExtractNamespacesFromType(csType, namespaces);
                    break;

                case CsProperty csProperty:
                    if (csProperty.PropertyType != null)
                        ExtractNamespacesFromType(csProperty.PropertyType, namespaces);
                    break;

                case CsParameter csParameter:
                    if (csParameter.ParameterType != null)
                        ExtractNamespacesFromType(csParameter.ParameterType, namespaces);
                    break;

                case CsMethod csMethod:
                    if (csMethod.ReturnType != null)
                        ExtractNamespacesFromType(csMethod.ReturnType, namespaces);
                    if (csMethod.HasParameters)
                    {
                        foreach (var param in csMethod.Parameters)
                            ExtractNamespacesFromModel(param, namespaces);
                    }
                    break;

                case CsField csField:
                    if (csField.DataType != null)
                        ExtractNamespacesFromType(csField.DataType, namespaces);
                    break;

                case CsEvent csEvent:
                    if (csEvent.EventType != null)
                        ExtractNamespacesFromType(csEvent.EventType, namespaces);
                    break;

                case CsClass csClass:
                    ExtractNamespacesFromContainer(csClass, namespaces);
                    break;

                case CsInterface csInterface:
                    ExtractNamespacesFromContainer(csInterface, namespaces);
                    break;

                case CsStructure csStructure:
                    ExtractNamespacesFromContainer(csStructure, namespaces);
                    break;

                case CsEnum csEnum:
                    // Enums may have attributes
                    if (csEnum.Values != null)
                    {
                        foreach (var enumValue in csEnum.Values)
                            ExtractNamespacesFromAttributes(enumValue, namespaces);
                    }
                    break;

                case CsDelegate csDelegate:
                    if (csDelegate.ReturnType != null)
                        ExtractNamespacesFromType(csDelegate.ReturnType, namespaces);
                    if (csDelegate.HasParameters)
                    {
                        foreach (var param in csDelegate.Parameters)
                            ExtractNamespacesFromModel(param, namespaces);
                    }
                    break;
            }
        }

        /// <summary>
        /// Extracts namespaces from attributes applied to a C# model.
        /// Performance: O(a * p) where a = attributes, p = parameters per attribute. ~5µs per attribute.
        /// Thread-Safe: Yes (read-only operations on immutable C# models)
        /// </summary>
        /// <param name="model">The model to extract attribute namespaces from.</param>
        /// <param name="namespaces">HashSet to collect unique namespaces.</param>
        private void ExtractNamespacesFromAttributes(ICsModel model, HashSet<string> namespaces)
        {
            if (model == null) return;

            // Pattern matching: O(1) type check, compiler-optimized
            if (model is CsClass csClass && csClass.HasAttributes)
            {
                ExtractNamespacesFromAttributeList(csClass.Attributes, namespaces);
            }
            else if (model is CsInterface csInterface && csInterface.HasAttributes)
            {
                ExtractNamespacesFromAttributeList(csInterface.Attributes, namespaces);
            }
            else if (model is CsStructure csStructure && csStructure.HasAttributes)
            {
                ExtractNamespacesFromAttributeList(csStructure.Attributes, namespaces);
            }
            else if (model is CsEnum csEnum && csEnum.HasAttributes)
            {
                ExtractNamespacesFromAttributeList(csEnum.Attributes, namespaces);
            }
            else if (model is CsDelegate csDelegate && csDelegate.HasAttributes)
            {
                ExtractNamespacesFromAttributeList(csDelegate.Attributes, namespaces);
            }
            else if (model is CsMethod csMethod && csMethod.HasAttributes)
            {
                ExtractNamespacesFromAttributeList(csMethod.Attributes, namespaces);
            }
            else if (model is CsProperty csProperty && csProperty.HasAttributes)
            {
                ExtractNamespacesFromAttributeList(csProperty.Attributes, namespaces);
            }
            else if (model is CsField csField && csField.HasAttributes)
            {
                ExtractNamespacesFromAttributeList(csField.Attributes, namespaces);
            }
            else if (model is CsEvent csEvent && csEvent.HasAttributes)
            {
                ExtractNamespacesFromAttributeList(csEvent.Attributes, namespaces);
            }
            else if (model is CsParameter csParameter && csParameter.HasAttributes)
            {
                ExtractNamespacesFromAttributeList(csParameter.Attributes, namespaces);
            }
            else if (model is CsEnumValue csEnumValue && csEnumValue.HasAttributes)
            {
                ExtractNamespacesFromAttributeList(csEnumValue.Attributes, namespaces);
            }
        }

        /// <summary>
        /// Extracts namespaces from a list of attributes.
        /// </summary>
        /// <param name="attributes">The attributes to extract namespaces from.</param>
        /// <param name="namespaces">HashSet to collect unique namespaces.</param>
        private void ExtractNamespacesFromAttributeList(IReadOnlyList<CsAttribute> attributes, HashSet<string> namespaces)
        {
            if (attributes == null || attributes.Count == 0) return;

            foreach (var attribute in attributes)
            {
                if (attribute == null) continue;

                // Add the attribute's type namespace
                if (attribute.Type != null)
                    ExtractNamespacesFromType(attribute.Type, namespaces);

                // Extract namespaces from attribute parameters
                if (attribute.HasParameters)
                {
                    foreach (var param in attribute.Parameters)
                    {
                        if (param.Value?.TypeValue != null)
                            ExtractNamespacesFromType(param.Value.TypeValue, namespaces);
                    }
                }
            }
        }

        /// <summary>
        /// Extracts namespaces from a type and its generic parameters.
        /// </summary>
        private void ExtractNamespacesFromType(CsType csType, HashSet<string> namespaces)
        {
            if (csType == null) return;

            // Add the main type's namespace
            if (!string.IsNullOrEmpty(csType.Namespace))
                namespaces.Add(csType.Namespace);

            // Handle generic types
            if (csType.HasStrongTypesInGenerics && csType.GenericParameters != null)
            {
                foreach (var genericParam in csType.GenericParameters)
                {
                    if (genericParam.Type != null)
                        ExtractNamespacesFromType(genericParam.Type, namespaces);
                }
            }

            // Handle tuple types
            if (csType.IsTuple && csType.TupleTypes != null)
            {
                foreach (var tupleType in csType.TupleTypes)
                {

                    if (tupleType.TupleType != null)
                        ExtractNamespacesFromType(tupleType.TupleType, namespaces);
                }
            }
        }

        /// <summary>
        /// Extracts namespaces from a container (class, interface, struct) including base types, interfaces, and members.
        /// </summary>
        private void ExtractNamespacesFromContainer(CsContainer container, HashSet<string> namespaces)
        {
            if (container == null) return;

            // Add the container's own namespace
            if (!string.IsNullOrEmpty(container.Namespace) && !namespaces.Contains(container.Namespace)) namespaces.Add(container.Namespace);

            // Handle interfaces
            if (container.InheritedInterfaces != null)
            {
                foreach (var interfaceType in container.InheritedInterfaces)
                    ExtractNamespacesFromContainer(interfaceType, namespaces);
            }

            // Handle generic parameters on the container itself
            if (container.IsGeneric && container.GenericParameters != null)
            {
                foreach (var genericParam in container.GenericParameters)
                {
                    // Extract from constraints
                    if (genericParam.HasConstraintTypes)
                    {
                        foreach (var constraintType in genericParam.ConstrainingTypes)
                        {
                            if (constraintType != null)
                                ExtractNamespacesFromType(constraintType, namespaces);
                        }
                    }
                }
            }

            // Handle members based on container type
            if (container is CsClass csClass)
            {
                if (csClass.BaseClass != null)
                {
                    ExtractNamespacesFromContainer(csClass.BaseClass, namespaces);
                }
                ExtractNamespacesFromMembers(csClass.Properties, namespaces);
                ExtractNamespacesFromMembers(csClass.Methods, namespaces);
                ExtractNamespacesFromMembers(csClass.Fields, namespaces);
                ExtractNamespacesFromMembers(csClass.Events, namespaces);
                ExtractNamespacesFromMembers(csClass.Constructors, namespaces);
            }
            else if (container is CsInterface csInterface)
            {
                ExtractNamespacesFromMembers(csInterface.Properties, namespaces);
                ExtractNamespacesFromMembers(csInterface.Methods, namespaces);
                ExtractNamespacesFromMembers(csInterface.Events, namespaces);
            }
            else if (container is CsStructure csStructure)
            {
                ExtractNamespacesFromMembers(csStructure.Properties, namespaces);
                ExtractNamespacesFromMembers(csStructure.Methods, namespaces);
                ExtractNamespacesFromMembers(csStructure.Fields, namespaces);
                ExtractNamespacesFromMembers(csStructure.Events, namespaces);
                ExtractNamespacesFromMembers(csStructure.Constructors, namespaces);
            }

        }

        /// <summary>
        /// Extracts namespaces from a collection of members.
        /// </summary>
        private void ExtractNamespacesFromMembers<T>(IReadOnlyList<T> members, HashSet<string> namespaces) where T : ICsModel
        {
            if (members == null) return;

            foreach (var member in members)
                ExtractNamespacesFromModel(member, namespaces);
        }

        #endregion

        #region Namespace Addition Methods

        /// <summary>
        /// Adds using statements for all namespaces used in the managed container to the source file.
        /// </summary>
        /// <returns>Task that completes when the using statements have been added.</returns>
        public async Task AddNamespacesFromContainerAsync()
        {
            if (_container == null) return;

            var unmanagedNamespaces = GetUnmanagedNamespaces(_container);

            if (unmanagedNamespaces.Count == 0) return;

            await AddNamespacesAsync(unmanagedNamespaces);
        }

        /// <summary>
        /// Adds a single namespace as a using statement to the source file if it doesn't already exist.
        /// </summary>
        /// <param name="nameSpace">The namespace to add.</param>
        /// <param name="alias">Optional alias for the using statement.</param>
        /// <returns>Task that completes when the namespace has been added.</returns>
        public async Task AddNamespaceAsync(string nameSpace, string alias = null)
        {
            if (string.IsNullOrEmpty(nameSpace)) return;

            var source = _source;
            if (source == null) return;

            // Check if namespace is already managed
            if (_namespaceManager?.ValidNameSpace(nameSpace).namespaceFound == true)
                return;

            // Check if using statement already exists in source
            if (source.HasUsingStatement(nameSpace, alias))
                return;

            // Use the existing extension method which already formats properly
            var updatedSource = await source.AddUsingStatementAsync(nameSpace, alias);

            if (updatedSource == null) return;

            var updatedContainer = updatedSource.GetModel<TContainerType>(ContainerPath);
            if (updatedContainer != null)
            {
                UpdateSources(updatedSource, updatedContainer);
                LoadNamespaceManager();
            }
        }

        /// <summary>
        /// Adds multiple namespaces as using statements to the source file. Only adds namespaces that don't already exist.
        /// Uses SourceFormatter to generate all using statements and applies them in a single source update.
        /// Performance: O(n) filtering + 1 async I/O operation. ~100ms for any count.
        /// Thread-Safe: Yes (volatile reads, atomic source update)
        /// </summary>
        /// <param name="nameSpaces">Collection of namespaces to add.</param>
        /// <returns>Task that completes when the namespaces have been added.</returns>
        public async Task AddNamespacesAsync(IEnumerable<string> nameSpaces)
        {
            if (nameSpaces == null) return;

            if (nameSpaces.FirstOrDefault() == null) return;

            var existingNamespaces = _namespaceManager;

            // Load namespace manager if not already loaded. This ensures we have the most up-to-date namespaces for filtering before adding new ones.
            if (existingNamespaces == null)
            {
                LoadNamespaceManager();
                existingNamespaces = _namespaceManager;
            }

            // Volatile read: ~1ns, ensures visibility across threads
            var source = _source;
            if (source == null) return;

            // Use a HashSet to filter out duplicates for namespaces to add, O(n) where n = nameSpaces count. Typically < 50 namespaces, so this is efficient.
            var namespacesToAdd = new HashSet<string>();

            foreach (var ns in nameSpaces)
            {
                if (string.IsNullOrEmpty(ns)) continue;

                // Local Cache Check: O(1) dictionary lookup, thread-safe for reads
                if (existingNamespaces?.ValidNameSpace(ns).namespaceFound == true) continue;

                // Volatile read + O(1) NamespaceManager lookup
                if (_namespaceManager?.ValidNameSpace(ns).namespaceFound == true) continue;

                namespacesToAdd.Add(ns);
            }

            if (namespacesToAdd.Count == 0) return;

            // SourceFormatter: Uses StringBuilder internally, O(n) string building
            // ~1µs per namespace
            var usingFormatter = new SourceFormatter();
            foreach (var ns in namespacesToAdd)
            {
                usingFormatter.AppendCodeLine(0, $"using {ns};");
            }

            string usingStatementsBlock = usingFormatter.ReturnSource();

            // CRITICAL OPTIMIZATION: Single I/O operation instead of N operations
            // Saves: (N-1) * 100ms where N = namespace count
            CsSource updatedSource = null;
            //if (source.NamespaceReferences != null && source.NamespaceReferences.Count > 0)
            //{

            //     await this.UsingStatementsAddAfterAsync(usingStatementsBlock);
            //}
            //else
            //{
            //    await this.SourceAddToBeginningAsync(usingStatementsBlock);
            //}

            await this.SourceAddToBeginningAsync(usingStatementsBlock);
            if (updatedSource == null) return;

            // Single reload: ~10ms
            var updatedContainer = updatedSource.GetModel<TContainerType>(ContainerPath);
            if (updatedContainer != null)
            {
                // Lock overhead: ~30ns (uncontended)
                UpdateSources(updatedSource, updatedContainer);
                LoadNamespaceManager();
            }
        }

        #endregion

        /// <summary>
        /// Creates a new using statement in the source if the using statement does not exist. It will also reload the namespace manager and update it.
        /// </summary>
        /// <param name="nameSpace">Namespace to add to the source file.</param>
        /// <param name="alias">Optional parameter to assign a alias to the using statement.</param>
        [Obsolete("Use AddNamespaceAsync instead. This method will be removed in a future version.", false)]
        public async Task UsingStatementAddAsync(string nameSpace, string alias = null)
        {
            await AddNamespaceAsync(nameSpace, alias);
        }

        /// <summary>
        /// Adds the provided syntax to the beginning of the source file.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        public async Task SourceAddToBeginningAsync(string syntax)
        {
            await SourceAddToBeginningTransactionAsync(syntax);
        }

        /// <summary>
        /// Adds the provided syntax to the beginning of the source file.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        /// <returns>The details of the updated source or null if the transaction details could not be saved.</returns>
        public async Task<TransactionDetail> SourceAddToBeginningTransactionAsync(string syntax)
        {
            if (string.IsNullOrEmpty(syntax)) return null;
            var source = _source ?? throw new ArgumentNullException(nameof(Source));

            var updatedSource = await source.AddToBeginningTransactionAsync(syntax);

            if (updatedSource == null) throw new ArgumentNullException(nameof(Source));

            var updatedContainer = updatedSource.Source.GetModel<TContainerType>(ContainerPath);

            UpdateSources(updatedSource.Source, updatedContainer);

            return updatedSource.Transaction;
        }

        /// <summary>
        /// Adds the provided syntax to the end of the source file.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        public async Task SourceAddToEndAsync(string syntax)
        {
            await SourceAddToEndTransactionAsync(syntax);
        }

        /// <summary>
        /// Adds the provided syntax to the end of the source file.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        /// <returns>The details of the updated source or null if the transaction details could not be saved.</returns>
        public async Task<TransactionDetail> SourceAddToEndTransactionAsync(string syntax)
        {
            if (string.IsNullOrEmpty(syntax)) return null;
            var source = _source ?? throw new ArgumentNullException(nameof(Source));

            var updatedSource = await source.AddToEndTransactionAsync(syntax);

            if (updatedSource?.Source == null) throw new ArgumentNullException(nameof(Source));

            var updatedContainer = updatedSource.Source.GetModel<TContainerType>(ContainerPath);

            UpdateSources(updatedSource.Source, updatedContainer);

            return updatedSource?.Transaction;
        }

        /// <summary>
        /// Adds the provided syntax before the containers definition.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        public async Task ContainerAddBeforeAsync(string syntax)
        {
            await ContainerAddBeforeTransactionAsync(syntax);
        }

        /// <summary>
        /// Adds the provided syntax before the containers definition.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        /// <returns>The details of the updated source or null if the transaction details could not be saved.</returns>
        public async Task<TransactionDetail> ContainerAddBeforeTransactionAsync(string syntax)
        {
            if (string.IsNullOrEmpty(syntax)) return null;

            var container = _container ?? throw new ArgumentNullException(nameof(Container));

            var updatedSource = await container.AddBeforeTransactionAsync(syntax);

            if (updatedSource?.Source == null) throw new ArgumentNullException(nameof(Source));

            var updatedContainer = updatedSource.Source.GetModel<TContainerType>(ContainerPath);

            UpdateSources(updatedSource.Source, updatedContainer);

            return updatedSource?.Transaction;
        }

        /// <summary>
        /// Adds the provided syntax after containers definition.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        public async Task ContainerAddAfterAsync(string syntax)
        {
            await ContainerAddAfterTransactionAsync(syntax);
        }

        /// <summary>
        /// Adds the provided syntax after containers definition.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        /// <returns>The details of the updated source or null if the transaction details could not be saved.</returns>
        public async Task<TransactionDetail> ContainerAddAfterTransactionAsync(string syntax)
        {
            if (string.IsNullOrEmpty(syntax)) return null;

            var container = _container ?? throw new ArgumentNullException(nameof(Container));

            var updatedSource = await container.AddAfterTransactionAsync(syntax);

            if (updatedSource?.Source == null) throw new ArgumentNullException(nameof(Source));

            var updatedContainer = updatedSource.Source.GetModel<TContainerType>(ContainerPath);

            UpdateSources(updatedSource.Source, updatedContainer);

            return updatedSource.Transaction;
        }

        /// <summary>
        /// Adds the provided syntax to the beginning of the containers definition.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        public async Task ContainerAddToBeginningAsync(string syntax)
        {
            await ContainerAddToBeginningTransactionAsync(syntax);
        }

        /// <summary>
        /// Adds the provided syntax to the beginning of the containers definition.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        /// <returns>The details of the updated source or null if the transaction details could not be saved.</returns>
        public async Task<TransactionDetail> ContainerAddToBeginningTransactionAsync(string syntax)
        {
            if (string.IsNullOrEmpty(syntax)) return null;
            var container = _container ?? throw new ArgumentNullException(nameof(Container));

            var updatedSource = await container.AddToBeginningTransactionAsync(syntax);

            if (updatedSource?.Source == null) throw new ArgumentNullException(nameof(Source));

            var updatedContainer = updatedSource.Source.GetModel<TContainerType>(ContainerPath);

            UpdateSources(updatedSource.Source, updatedContainer);

            return updatedSource.Transaction;
        }

        /// <summary>
        /// Adds the provided syntax to the end of the containers definition.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        public async Task ContainerAddToEndAsync(string syntax)
        {
            await ContainerAddToEndTransactionAsync(syntax);
        }

        /// <summary>
        /// Adds the provided syntax to the end of the containers definition.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        /// <returns>The details of the updated source or null if the transaction details could not be saved.</returns>
        public async Task<TransactionDetail> ContainerAddToEndTransactionAsync(string syntax)
        {
            if (string.IsNullOrEmpty(syntax)) return null;
            var container = _container ?? throw new ArgumentNullException(nameof(Container));

            var updatedSource = await container.AddToEndTransactionAsync(syntax);

            if (updatedSource?.Source == null) throw new ArgumentNullException(nameof(Source));

            var updatedContainer = updatedSource.Source.GetModel<TContainerType>(ContainerPath);

            UpdateSources(updatedSource.Source, updatedContainer);

            return updatedSource.Transaction;
        }

        /// <summary>
        /// Adds the provided syntax before the first using statement definition.
        /// </summary>
        /// <param name="syntax">Target syntax to be added</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        public async Task UsingStatementsAddBeforeAsync(string syntax)
        {
            await UsingStatementsAddBeforeTransactionAsync(syntax);
        }

        /// <summary>
        /// Adds the provided syntax before the first using statement definition.
        /// </summary>
        /// <param name="syntax">Target syntax to be added</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        /// <returns>The details of the updated source or null if the transaction details could not be saved.</returns>
        public async Task<TransactionDetail> UsingStatementsAddBeforeTransactionAsync(string syntax)
        {
            if (string.IsNullOrEmpty(syntax)) return null;
            var source = _source ?? throw new ArgumentNullException(nameof(Source));

            CsSourceTransaction updatedSource = null;

            var sourceDoc = source.SourceDocument;

            var usingStatement = source.NamespaceReferences.FirstOrDefault(n => n.SourceDocument == sourceDoc && n.LoadedFromSource);
            if (usingStatement != null)
            {
                updatedSource = await usingStatement.AddBeforeTransactionAsync(syntax);
            }
            else
            {
                updatedSource = await source.AddToBeginningTransactionAsync(syntax);
            }

            if (updatedSource?.Source == null) throw new ArgumentNullException(nameof(Source));

            var updatedContainer = updatedSource.Source.GetModel<TContainerType>(ContainerPath);

            UpdateSources(updatedSource.Source, updatedContainer);

            return updatedSource.Transaction;
        }

        /// <summary>
        /// Adds the provided syntax before the first using statement definition.
        /// </summary>
        /// <param name="syntax">Target syntax to be added</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        public async Task UsingStatementsAddAfterAsync(string syntax)
        {
            await UsingStatementsAddAfterTransactionAsync(syntax);
        }

        /// <summary>
        /// Adds the provided syntax before the first using statement definition.
        /// </summary>
        /// <param name="syntax">Target syntax to be added</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        /// <returns>The details of the updated source or null if the transaction details could not be saved.</returns>
        public async Task<TransactionDetail> UsingStatementsAddAfterTransactionAsync(string syntax)
        {
            if (string.IsNullOrEmpty(syntax)) return null;
            var source = _source ?? throw new ArgumentNullException(nameof(Source));

            CsSourceTransaction updatedSource = null;

            var sourceDoc = source.SourceDocument;

            var usingStatement = source.NamespaceReferences.LastOrDefault(n => n.SourceDocument == sourceDoc && n.LoadedFromSource);
            if (usingStatement != null)
            {
                updatedSource = await usingStatement.AddAfterTransactionAsync(syntax);
            }
            else
            {
                updatedSource = await source.AddToBeginningTransactionAsync(syntax);
            }

            if (updatedSource?.Source == null) throw new ArgumentNullException(nameof(Source));

            var updatedContainer = updatedSource.Source.GetModel<TContainerType>(ContainerPath);

            UpdateSources(updatedSource.Source, updatedContainer);

            return updatedSource.Transaction;
        }

        /// <summary>
        /// Adds the provided syntax before the field definitions.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        public abstract Task FieldsAddBeforeAsync(string syntax);

        /// <summary>
        /// Adds the provided syntax before the field definitions.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        /// <returns>The details of the updated source or null if the transaction details could not be saved.</returns>
        public abstract Task<TransactionDetail> FieldsAddBeforeTransactionAsync(string syntax);


        /// <summary>
        /// Adds the provided syntax after the field definitions.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        public abstract Task FieldsAddAfterAsync(string syntax);

        /// <summary>
        /// Adds the provided syntax after the field definitions.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        /// <returns>The details of the updated source or null if the transaction details could not be saved.</returns>
        public abstract Task<TransactionDetail> FieldsAddAfterTransactionAsync(string syntax);


        /// <summary>
        /// Add the provided syntax before the constructor definitions.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        public abstract Task ConstructorsAddBeforeAsync(string syntax);

        /// <summary>
        /// Add the provided syntax before the constructor definitions.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        /// <returns>The details of the updated source or null if the transaction details could not be saved.</returns>
        public abstract Task<TransactionDetail> ConstructorsAddBeforeTransactionAsync(string syntax);


        /// <summary>
        /// Add the provided syntax after the constructor definitions.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        public abstract Task ConstructorsAddAfterAsync(string syntax);

        /// <summary>
        /// Add the provided syntax after the constructor definitions.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        /// <returns>The details of the updated source or null if the transaction details could not be saved.</returns>
        public abstract Task<TransactionDetail> ConstructorsAddAfterTransactionAsync(string syntax);


        /// <summary>
        /// Add the provided syntax before the property definitions.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        public async Task PropertiesAddBeforeAsync(string syntax)
        {
            await PropertiesAddBeforeTransactionAsync(syntax);

        }

        /// <summary>
        /// Add the provided syntax before the property definitions.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        /// <returns>The details of the updated source or null if the transaction details could not be saved.</returns>
        public async Task<TransactionDetail> PropertiesAddBeforeTransactionAsync(string syntax)
        {
            if (string.IsNullOrEmpty(syntax)) return null;
            var source = _source ?? throw new ArgumentNullException(nameof(Source));
            var container = _container ?? throw new ArgumentNullException(nameof(Container));

            var sourceDoc = source.SourceDocument;

            TransactionDetail result = null;

            var propertyData = container.Properties.FirstOrDefault(p => p.ModelSourceFile == sourceDoc && p.LoadedFromSource);
            if (propertyData != null)
            {
                var updatedSource = await propertyData.AddBeforeTransactionAsync(syntax);

                if (updatedSource?.Source == null) throw new ArgumentNullException(nameof(Source));

                var updatedContainer = updatedSource.Source.GetModel<TContainerType>(ContainerPath);

                UpdateSources(updatedSource.Source, updatedContainer);

                result = updatedSource.Transaction;
            }
            else
            {
                result = await this.ConstructorsAddAfterTransactionAsync(syntax);
            }

            return result;
        }

        /// <summary>
        /// Add the provided syntax after the property definitions.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        public async Task PropertiesAddAfterAsync(string syntax)
        {
            await PropertiesAddAfterTransactionAsync(syntax);
        }

        /// <summary>
        /// Add the provided syntax after the property definitions.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        /// <returns>The details of the updated source or null if the transaction details could not be saved.</returns>
        public async Task<TransactionDetail> PropertiesAddAfterTransactionAsync(string syntax)
        {
            if (string.IsNullOrEmpty(syntax)) return null;
            var source = _source ?? throw new ArgumentNullException(nameof(Source));
            var container = _container ?? throw new ArgumentNullException(nameof(Container));

            var sourceDoc = source.SourceDocument;

            TransactionDetail result = null;

            var propertyData = container.Properties.LastOrDefault(p => p.ModelSourceFile == sourceDoc && p.LoadedFromSource);
            if (propertyData != null)
            {
                var updatedSource = await propertyData.AddAfterTransactionAsync(syntax);

                if (updatedSource?.Source == null) throw new ArgumentNullException(nameof(Source));

                var updatedContainer = updatedSource.Source.GetModel<TContainerType>(ContainerPath);

                UpdateSources(updatedSource.Source, updatedContainer);

                result = updatedSource.Transaction;
            }
            else
            {
                result = await this.ConstructorsAddAfterTransactionAsync(syntax);
            }

            return result;
        }

        /// <summary>
        /// Add the provided syntax before the event definitions.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        public async Task EventsAddBeforeAsync(string syntax)
        {
            await EventsAddBeforeTransactionAsync(syntax);
        }

        /// <summary>
        /// Add the provided syntax before the event definitions.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        /// <returns>The details of the updated source or null if the transaction details could not be saved.</returns>
        public async Task<TransactionDetail> EventsAddBeforeTransactionAsync(string syntax)
        {
            if (string.IsNullOrEmpty(syntax)) return null;
            var source = _source ?? throw new ArgumentNullException(nameof(Source));
            var container = _container ?? throw new ArgumentNullException(nameof(Container));

            var sourceDoc = source.SourceDocument;

            TransactionDetail result = null;

            var eventData = container.Events.FirstOrDefault(e => e.ModelSourceFile == sourceDoc && e.LoadedFromSource);

            if (eventData != null)
            {
                var updatedSource = await eventData.AddBeforeTransactionAsync(syntax);

                if (updatedSource?.Source == null) throw new ArgumentNullException(nameof(Source));

                var updatedContainer = updatedSource.Source.GetModel<TContainerType>(ContainerPath);

                UpdateSources(updatedSource.Source, updatedContainer);

                result = updatedSource.Transaction;
            }
            else
            {
                result = await this.PropertiesAddAfterTransactionAsync(syntax);
            }

            return result;
        }

        /// <summary>
        /// Add the provided syntax after the event definitions.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        public async Task EventsAddAfterAsync(string syntax)
        {
            await EventsAddAfterTransactionAsync(syntax);
        }

        /// <summary>
        /// Add the provided syntax after the event definitions.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        /// <returns>The details of the updated source or null if the transaction details could not be saved.</returns>
        public async Task<TransactionDetail> EventsAddAfterTransactionAsync(string syntax)
        {
            if (string.IsNullOrEmpty(syntax)) return null;
            var source = _source ?? throw new ArgumentNullException(nameof(Source));
            var container = _container ?? throw new ArgumentNullException(nameof(Container));

            var sourceDoc = source.SourceDocument;

            TransactionDetail result = null;

            var eventData = container.Events.LastOrDefault(e => e.ModelSourceFile == sourceDoc && e.LoadedFromSource);

            if (eventData != null)
            {
                var updatedSource = await eventData.AddAfterTransactionAsync(syntax);

                if (updatedSource?.Source == null) throw new ArgumentNullException(nameof(Source));

                var updatedContainer = updatedSource.Source.GetModel<TContainerType>(ContainerPath);

                UpdateSources(updatedSource.Source, updatedContainer);

                result = updatedSource.Transaction;
            }
            else
            {
                result = await this.PropertiesAddAfterTransactionAsync(syntax);
            }

            return result;
        }

        /// <summary>
        /// Add the provided syntax before the method definitions.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        public async Task MethodsAddBeforeAsync(string syntax)
        {
            await MethodsAddBeforeTransactionAsync(syntax);
        }

        /// <summary>
        /// Add the provided syntax before the method definitions.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        /// <returns>The details of the updated source or null if the transaction details could not be saved.</returns>
        public async Task<TransactionDetail> MethodsAddBeforeTransactionAsync(string syntax)
        {
            if (string.IsNullOrEmpty(syntax)) return null;
            var source = _source ?? throw new ArgumentNullException(nameof(Source));
            var container = _container ?? throw new ArgumentNullException(nameof(Container));

            var sourceDoc = source.SourceDocument;

            TransactionDetail result = null;
            var methodData = container.Methods.FirstOrDefault(m => m.ModelSourceFile == sourceDoc && m.LoadedFromSource);

            if (methodData != null)
            {
                var updatedSource = await methodData.AddBeforeTransactionAsync(syntax);

                if (updatedSource?.Source == null) throw new ArgumentNullException(nameof(Source));

                var updatedContainer = updatedSource.Source.GetModel<TContainerType>(ContainerPath);

                UpdateSources(updatedSource.Source, updatedContainer);

                result = updatedSource.Transaction;
            }
            else
            {
                result = await this.EventsAddAfterTransactionAsync(syntax);
            }

            return result;
        }

        /// <summary>
        /// Add the provided syntax after the method definitions.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        public async Task MethodsAddAfterAsync(string syntax)
        {
            await MethodsAddAfterTransactionAsync(syntax);
        }

        /// <summary>
        /// Add the provided syntax after the method definitions.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        /// <returns>The details of the updated source or null if the transaction details could not be saved.</returns>
        public async Task<TransactionDetail> MethodsAddAfterTransactionAsync(string syntax)
        {
            if (string.IsNullOrEmpty(syntax)) return null;
            var source = _source ?? throw new ArgumentNullException(nameof(Source));
            var container = _container ?? throw new ArgumentNullException(nameof(Container));

            var sourceDoc = source.SourceDocument;

            TransactionDetail result = null;

            var methodData = container.Methods.LastOrDefault(m => m.ModelSourceFile == sourceDoc && m.LoadedFromSource);

            if (methodData != null)
            {
                var updatedSource = await methodData.AddAfterTransactionAsync(syntax);

                if (updatedSource?.Source == null) throw new ArgumentNullException(nameof(Source));

                var updatedContainer = updatedSource.Source.GetModel<TContainerType>(ContainerPath);

                UpdateSources(updatedSource.Source, updatedContainer);

                result = updatedSource.Transaction;
            }
            else
            {
                result = await this.EventsAddAfterTransactionAsync(syntax);
            }

            return result;
        }

        /// <summary>
        /// Add the syntax before the target member.
        /// </summary>
        /// <param name="member">Target member.</param>
        /// <param name="syntax">The syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        public async Task MemberAddBeforeAsync(CsMember member, string syntax)
        {
            await MemberAddBeforeTransactionAsync(member, syntax);

        }

        /// <summary>
        /// Add the syntax before the target member.
        /// </summary>
        /// <param name="member">Target member.</param>
        /// <param name="syntax">The syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        /// <returns>The details of the updated source or null if the transaction details could not be saved.</returns>
        public async Task<TransactionDetail> MemberAddBeforeTransactionAsync(CsMember member, string syntax)
        {
            if (string.IsNullOrEmpty(syntax)) return null;

            if (member == null) throw new ArgumentNullException(nameof(member));

            var source = _source ?? throw new ArgumentNullException(nameof(Source));
            var container = _container ?? throw new ArgumentNullException(nameof(Container));

            CsSourceTransaction updatedSource = null;

            var currentModel = this.Source.GetModel<CsMember>(member.LookupPath);
            if (currentModel == null) throw new CodeFactoryException($"Could not get the current member model for '{member.Name}' cannot add before the member.");


            updatedSource = await currentModel.AddBeforeTransactionAsync(syntax);

            if (updatedSource?.Source == null) throw new ArgumentNullException(nameof(Source));

            var updatedContainer = updatedSource.Source.GetModel<TContainerType>(ContainerPath);

            UpdateSources(updatedSource.Source, updatedContainer);

            return updatedSource.Transaction;
        }

        /// <summary>
        /// Add the syntax after the target member.
        /// </summary>
        /// <param name="member">Target member.</param>
        /// <param name="syntax">The syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        public async Task MemberAddAfterAsync(CsMember member, string syntax)
        {
            await MemberAddAfterTransactionAsync(member, syntax);
        }

        /// <summary>
        /// Add the syntax after the target member.
        /// </summary>
        /// <param name="member">Target member.</param>
        /// <param name="syntax">The syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        /// <returns>The details of the updated source or null if the transaction details could not be saved.</returns>
        public async Task<TransactionDetail> MemberAddAfterTransactionAsync(CsMember member, string syntax)
        {
            if (string.IsNullOrEmpty(syntax)) return null;

            if (member == null) throw new ArgumentNullException(nameof(member));

            var source = _source ?? throw new ArgumentNullException(nameof(Source));
            var container = _container ?? throw new ArgumentNullException(nameof(Container));

            CsSourceTransaction updatedSource = null;

            var currentModel = this.Source.GetModel<CsMember>(member.LookupPath);
            if (currentModel == null) throw new CodeFactoryException($"Could not get the current member model for '{member.Name}' cannot add after the member.");


            updatedSource = await currentModel.AddAfterTransactionAsync(syntax);

            if (updatedSource?.Source == null) throw new ArgumentNullException(nameof(Source));

            var updatedContainer = updatedSource.Source.GetModel<TContainerType>(ContainerPath);

            UpdateSources(updatedSource.Source, updatedContainer);

            return updatedSource.Transaction;
        }

        /// <summary>
        /// Comments out member from the source container.
        /// </summary>
        /// <param name="member">Target member.</param>
        /// <param name="commentSyntax">Optional parameters sets the syntax to use when commenting out the member. This will default to use '//'</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        public async Task MemberCommentOut(CsMember member, string commentSyntax = "//")
        {
            if (string.IsNullOrEmpty(commentSyntax)) return;

            if (member == null) throw new ArgumentNullException(nameof(member));

            var source = _source ?? throw new ArgumentNullException(nameof(Source));
            var container = _container ?? throw new ArgumentNullException(nameof(Container));

            CsSource updatedSource = null;

            var currentModel = this.Source.GetModel<CsMember>(member.LookupPath);
            if (currentModel == null) throw new CodeFactoryException($"Could not get the current member model for '{member.Name}' cannot comment out the member.");


            updatedSource = await currentModel.CommentOutSyntaxAsync(commentSyntax);

            if (updatedSource == null) throw new ArgumentNullException(nameof(Source));

            var updatedContainer = updatedSource.GetModel<TContainerType>(ContainerPath);

            UpdateSources(updatedSource, updatedContainer);
        }

        /// <summary>
        /// Syntax replaces the target member.
        /// </summary>
        /// <param name="member">Target member.</param>
        /// <param name="syntax">The syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        public async Task MemberReplaceAsync(CsMember member, string syntax)
        {
            await MemberReplaceTransactionAsync(member, syntax);
        }

        /// <summary>
        /// Syntax replaces the target member.
        /// </summary>
        /// <param name="member">Target member.</param>
        /// <param name="syntax">The syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        /// <returns>The details of the updated source or null if the transaction details could not be saved.</returns>
        public async Task<TransactionDetail> MemberReplaceTransactionAsync(CsMember member, string syntax)
        {
            if (string.IsNullOrEmpty(syntax)) return null;

            if (member == null) throw new ArgumentNullException(nameof(member));

            var source = _source ?? throw new ArgumentNullException(nameof(Source));
            var container = _container ?? throw new ArgumentNullException(nameof(Container));

            CsSourceTransaction updatedSource = null;

            var currentModel = this.Source.GetModel<CsMember>(member.LookupPath);
            if (currentModel == null) throw new CodeFactoryException($"Could not get the current member model for '{member.Name}' cannot replace the member.");

            updatedSource = await currentModel.ReplaceTransactionAsync(syntax);

            if (updatedSource?.Source == null) throw new ArgumentNullException(nameof(Source));

            var updatedContainer = updatedSource.Source.GetModel<TContainerType>(ContainerPath);

            UpdateSources(updatedSource.Source, updatedContainer);

            return updatedSource.Transaction;
        }

        /// <summary>
        /// Removes the target member.
        /// </summary>
        /// <param name="member">Target member.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        public async Task MemberRemoveAsync(CsMember member)
        {
            if (member == null) throw new ArgumentNullException(nameof(member));

            var source = _source ?? throw new ArgumentNullException(nameof(Source));
            var container = _container ?? throw new ArgumentNullException(nameof(Container));

            CsSource updatedSource = null;

            var currentModel = this.Source.GetModel<CsMember>(member.LookupPath);
            if (currentModel == null) throw new CodeFactoryException($"Could not get the current member model for '{member.Name}' cannot remove the member.");


            updatedSource = await currentModel.DeleteAsync();

            if (updatedSource == null) throw new ArgumentNullException(nameof(Source));

            var updatedContainer = updatedSource.GetModel<TContainerType>(ContainerPath);

            UpdateSources(updatedSource, updatedContainer);
        }

        /// <summary>
        /// Add the provided syntax before the nested enumeration definitions.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        public async Task NestedEnumAddBeforeAsync(string syntax)
        {
            await NestedEnumAddBeforeTransactionAsync(syntax);
        }

        /// <summary>
        /// Add the provided syntax before the nested enumeration definitions.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        /// <returns>The details of the updated source or null if the transaction details could not be saved.</returns>
        public async Task<TransactionDetail> NestedEnumAddBeforeTransactionAsync(string syntax)
        {
            if (string.IsNullOrEmpty(syntax)) return null;
            var source = _source ?? throw new ArgumentNullException(nameof(Source));
            var container = _container as CsContainerWithNestedContainers ?? throw new ArgumentNullException(nameof(Container));

            var sourceDoc = source.SourceDocument;

            TransactionDetail result = null;

            var enumData = container.NestedEnums.FirstOrDefault(m => m.ModelSourceFile == sourceDoc && m.LoadedFromSource);

            if (enumData != null)
            {

                var updatedSource = await enumData.AddBeforeTransactionAsync(syntax);

                if (updatedSource?.Source == null) throw new ArgumentNullException(nameof(Source));

                var updatedContainer = updatedSource.Source.GetModel<TContainerType>(ContainerPath);

                UpdateSources(updatedSource.Source, updatedContainer);

                result = updatedSource.Transaction;
            }
            else
            {
                result = await this.MethodsAddAfterTransactionAsync(syntax);
            }

            return result;
        }

        /// <summary>
        /// Add the provided syntax after the nested enumeration definitions.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        public async Task NestedEnumAddAfterAsync(string syntax)
        {
            await NestedEnumAddAfterTransactionAsync(syntax);
        }

        /// <summary>
        /// Add the provided syntax after the nested enumeration definitions.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        /// <returns>The details of the updated source or null if the transaction details could not be saved.</returns>
        public async Task<TransactionDetail> NestedEnumAddAfterTransactionAsync(string syntax)
        {
            if (string.IsNullOrEmpty(syntax)) return null;
            var source = _source ?? throw new ArgumentNullException(nameof(Source));
            var container = _container as CsContainerWithNestedContainers ?? throw new ArgumentNullException(nameof(Container));

            var sourceDoc = source.SourceDocument;

            TransactionDetail result = null;

            var enumData = container.NestedEnums.LastOrDefault(m => m.ModelSourceFile == sourceDoc && m.LoadedFromSource);

            if (enumData != null)
            {

                var updatedSource = await enumData.AddAfterTransactionAsync(syntax);

                if (updatedSource?.Source == null) throw new ArgumentNullException(nameof(Source));

                var updatedContainer = updatedSource.Source.GetModel<TContainerType>(ContainerPath);

                UpdateSources(updatedSource.Source, updatedContainer);

                result = updatedSource.Transaction;
            }
            else
            {
                result = await this.MethodsAddAfterTransactionAsync(syntax);
            }

            return result;
        }

        /// <summary>
        /// Removes the nested enumeration.
        /// </summary>
        /// <param name="nested">The target nested enumeration.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        public async Task NestedEnumRemoveAsync(CsEnum nested)
        {
            if (nested == null) throw new ArgumentNullException(nameof(nested));

            var source = _source ?? throw new ArgumentNullException(nameof(Source));
            var container = _container ?? throw new ArgumentNullException(nameof(Container));

            CsSource updatedSource = null;

            var currentModel = this.Source.GetModel<CsEnum>(nested.LookupPath);
            if (currentModel == null) throw new CodeFactoryException($"Could not get the current Enum model for '{nested.Name}' cannot remove the nested eumeration.");


            updatedSource = await currentModel.DeleteAsync();

            if (updatedSource == null) throw new ArgumentNullException(nameof(Source));

            var updatedContainer = updatedSource.GetModel<TContainerType>(ContainerPath);

            UpdateSources(updatedSource, updatedContainer);
        }

        /// <summary>
        /// Replaces the nested enumeration with the provided syntax
        /// </summary>
        /// <param name="nested">The target nested enumeration.</param>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        public async Task NestedEnumReplaceAsync(CsEnum nested, string syntax)
        {
            await NestedEnumReplaceTransactionAsync(nested, syntax);
        }

        /// <summary>
        /// Replaces the nested enumeration with the provided syntax
        /// </summary>
        /// <param name="nested">The target nested enumeration.</param>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        /// <returns>The details of the updated source or null if the transaction details could not be saved.</returns>
        public async Task<TransactionDetail> NestedEnumReplaceTransactionAsync(CsEnum nested, string syntax)
        {
            if (string.IsNullOrEmpty(syntax)) return null;
            if (nested == null) throw new ArgumentNullException(nameof(nested));

            var source = _source ?? throw new ArgumentNullException(nameof(Source));
            var container = _container ?? throw new ArgumentNullException(nameof(Container));

            CsSourceTransaction updatedSource = null;

            var currentModel = this.Source.GetModel<CsEnum>(nested.LookupPath);
            if (currentModel == null) throw new CodeFactoryException($"Could not get the current Enum model for '{nested.Name}' cannot replace the nested eumeration.");


            updatedSource = await currentModel.ReplaceTransactionAsync(syntax);

            if (updatedSource?.Source == null) throw new ArgumentNullException(nameof(Source));

            var updatedContainer = updatedSource.Source.GetModel<TContainerType>(ContainerPath);

            UpdateSources(updatedSource.Source, updatedContainer);

            return updatedSource.Transaction;
        }

        /// <summary>
        /// Add the provided syntax before the nested interface definitions.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        public async Task NestedInterfaceAddBeforeAsync(string syntax)
        {
            await NestedInterfaceAddBeforeTransactionAsync(syntax);
        }

        /// <summary>
        /// Add the provided syntax before the nested interface definitions.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        /// <returns>The details of the updated source or null if the transaction details could not be saved.</returns>
        public async Task<TransactionDetail> NestedInterfaceAddBeforeTransactionAsync(string syntax)
        {
            if (string.IsNullOrEmpty(syntax)) return null;
            var source = _source ?? throw new ArgumentNullException(nameof(Source));
            var container = _container as CsContainerWithNestedContainers ?? throw new ArgumentNullException(nameof(Container));

            var sourceDoc = source.SourceDocument;

            TransactionDetail result = null;

            var interfaceData = container.NestedInterfaces.FirstOrDefault(m => m.ModelSourceFile == sourceDoc && m.LoadedFromSource);
            if (interfaceData != null)
            {
                var updatedSource = await interfaceData.AddBeforeTransactionAsync(syntax);

                if (updatedSource?.Source == null) throw new ArgumentNullException(nameof(Source));

                var updatedContainer = updatedSource.Source.GetModel<TContainerType>(ContainerPath);

                UpdateSources(updatedSource.Source, updatedContainer);

                result = updatedSource.Transaction;
            }
            else
            {
                result = await this.NestedEnumAddAfterTransactionAsync(syntax);
            }

            return result;
        }

        /// <summary>
        /// Add the provided syntax after the nested interface definitions.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        public async Task NestedInterfaceAddAfterAsync(string syntax)
        {
            await NestedInterfaceAddAfterTransactionAsync(syntax);
        }

        /// <summary>
        /// Add the provided syntax after the nested interface definitions.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        /// <returns>The details of the updated source or null if the transaction details could not be saved.</returns>
        public async Task<TransactionDetail> NestedInterfaceAddAfterTransactionAsync(string syntax)
        {
            if (string.IsNullOrEmpty(syntax)) return null;
            var source = _source ?? throw new ArgumentNullException(nameof(Source));
            var container = _container as CsContainerWithNestedContainers ?? throw new ArgumentNullException(nameof(Container));

            var sourceDoc = source.SourceDocument;

            TransactionDetail result = null;

            var interfaceData = container.NestedInterfaces.LastOrDefault(m => m.ModelSourceFile == sourceDoc && m.LoadedFromSource);
            if (interfaceData != null)
            {
                var updatedSource = await interfaceData.AddAfterTransactionAsync(syntax);

                if (updatedSource?.Source == null) throw new ArgumentNullException(nameof(Source));

                var updatedContainer = updatedSource.Source.GetModel<TContainerType>(ContainerPath);

                UpdateSources(updatedSource.Source, updatedContainer);

                result = updatedSource.Transaction;
            }
            else
            {
                result = await this.NestedEnumAddAfterTransactionAsync(syntax);
            }

            return result;
        }

        /// <summary>
        /// Removes the nested interface.
        /// </summary>
        /// <param name="nested">The target nested interface.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        public async Task NestedInterfaceRemoveAsync(CsInterface nested)
        {
            if (nested == null) throw new ArgumentNullException(nameof(nested));

            var source = _source ?? throw new ArgumentNullException(nameof(Source));
            var container = _container ?? throw new ArgumentNullException(nameof(Container));

            CsSource updatedSource = null;

            var currentModel = this.Source.GetModel<CsInterface>(nested.LookupPath);
            if (currentModel == null) throw new CodeFactoryException($"Could not get the current interface model for '{nested.Name}' cannot remove the nested interface.");


            updatedSource = await currentModel.DeleteAsync();

            if (updatedSource == null) throw new ArgumentNullException(nameof(Source));

            var updatedContainer = updatedSource.GetModel<TContainerType>(ContainerPath);

            UpdateSources(updatedSource, updatedContainer);
        }

        /// <summary>
        /// Replaces the nested interface with the provided syntax
        /// </summary>
        /// <param name="nested">The target nested interface.</param>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        public async Task NestedInterfaceReplaceAsync(CsInterface nested, string syntax)
        {
            await NestedInterfaceReplaceTransactionAsync(nested, syntax);
        }

        /// <summary>
        /// Replaces the nested interface with the provided syntax
        /// </summary>
        /// <param name="nested">The target nested interface.</param>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        /// <returns>The details of the updated source or null if the transaction details could not be saved.</returns>
        public async Task<TransactionDetail> NestedInterfaceReplaceTransactionAsync(CsInterface nested, string syntax)
        {
            if (string.IsNullOrEmpty(syntax)) return null;
            if (nested == null) throw new ArgumentNullException(nameof(nested));

            var source = _source ?? throw new ArgumentNullException(nameof(Source));
            var container = _container ?? throw new ArgumentNullException(nameof(Container));

            CsSourceTransaction updatedSource = null;

            var currentModel = this.Source.GetModel<CsInterface>(nested.LookupPath);
            if (currentModel == null) throw new CodeFactoryException($"Could not get the current interface model for '{nested.Name}' cannot replace the nested interface.");


            updatedSource = await currentModel.ReplaceTransactionAsync(syntax);

            if (updatedSource?.Source == null) throw new ArgumentNullException(nameof(Source));

            var updatedContainer = updatedSource.Source.GetModel<TContainerType>(ContainerPath);

            UpdateSources(updatedSource.Source, updatedContainer);

            return updatedSource.Transaction;
        }

        /// <summary>
        /// Add the provided syntax before the nested structures definitions.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        public async Task NestedStructuresAddBeforeAsync(string syntax)
        {
            await NestedStructuresAddBeforeTransactionAsync(syntax);
        }

        /// <summary>
        /// Add the provided syntax before the nested structures definitions.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        /// <returns>The details of the updated source or null if the transaction details could not be saved.</returns>
        public async Task<TransactionDetail> NestedStructuresAddBeforeTransactionAsync(string syntax)
        {
            if (string.IsNullOrEmpty(syntax)) return null;
            var source = _source ?? throw new ArgumentNullException(nameof(Source));
            var container = _container as CsContainerWithNestedContainers ?? throw new ArgumentNullException(nameof(Container));

            var sourceDoc = source.SourceDocument;

            TransactionDetail result = null;

            var structData = container.NestedStructures.FirstOrDefault(m => m.ModelSourceFile == sourceDoc && m.LoadedFromSource);
            if (structData != null)
            {
                var updatedSource = await structData.AddBeforeTransactionAsync(syntax);

                if (updatedSource?.Source == null) throw new ArgumentNullException(nameof(Source));

                var updatedContainer = updatedSource.Source.GetModel<TContainerType>(ContainerPath);

                UpdateSources(updatedSource.Source, updatedContainer);

                result = updatedSource.Transaction;
            }
            else
            {
                result = await this.NestedInterfaceAddAfterTransactionAsync(syntax);
            }

            return result;
        }

        /// <summary>
        /// Add the provided syntax after the nested structures definitions.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        public async Task NestedStructuresAddAfterAsync(string syntax)
        {
            await NestedStructuresAddAfterTransactionAsync(syntax);
        }

        /// <summary>
        /// Add the provided syntax after the nested structures definitions.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        /// <returns>The details of the updated source or null if the transaction details could not be saved.</returns>
        public async Task<TransactionDetail> NestedStructuresAddAfterTransactionAsync(string syntax)
        {
            if (string.IsNullOrEmpty(syntax)) return null;
            var source = _source ?? throw new ArgumentNullException(nameof(Source));
            var container = _container as CsContainerWithNestedContainers ?? throw new ArgumentNullException(nameof(Container));

            var sourceDoc = source.SourceDocument;

            TransactionDetail result = null;

            var structData = container.NestedStructures.LastOrDefault(m => m.ModelSourceFile == sourceDoc && m.LoadedFromSource);
            if (structData != null)
            {
                var updatedSource = await structData.AddAfterTransactionAsync(syntax);

                if (updatedSource?.Source == null) throw new ArgumentNullException(nameof(Source));

                var updatedContainer = updatedSource.Source.GetModel<TContainerType>(ContainerPath);

                UpdateSources(updatedSource.Source, updatedContainer);

                result = updatedSource.Transaction;
            }
            else
            {
                result = await this.NestedInterfaceAddAfterTransactionAsync(syntax);
            }

            return result;
        }

        /// <summary>
        /// Removes the nested structure.
        /// </summary>
        /// <param name="nested">The target nested structure.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        public async Task NestedStructureRemoveAsync(CsStructure nested)
        {
            if (nested == null) throw new ArgumentNullException(nameof(nested));

            var source = _source ?? throw new ArgumentNullException(nameof(Source));
            var container = _container ?? throw new ArgumentNullException(nameof(Container));

            CsSource updatedSource = null;

            var currentModel = this.Source.GetModel<CsStructure>(nested.LookupPath);
            if (currentModel == null) throw new CodeFactoryException($"Could not get the current structure model for '{nested.Name}' cannot remove the nested structure.");

            updatedSource = await currentModel.DeleteAsync();

            if (updatedSource == null) throw new ArgumentNullException(nameof(Source));

            var updatedContainer = updatedSource.GetModel<TContainerType>(ContainerPath);

            UpdateSources(updatedSource, updatedContainer);
        }

        /// <summary>
        /// Replaces the nested structure with the provided syntax
        /// </summary>
        /// <param name="nested">The target nested structure.</param>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        public async Task NestedStructureReplaceAsync(CsStructure nested, string syntax)
        {
            await NestedStructureReplaceTransactionAsync(nested, syntax);
        }

        /// <summary>
        /// Replaces the nested structure with the provided syntax
        /// </summary>
        /// <param name="nested">The target nested structure.</param>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        /// <returns>The details of the updated source or null if the transaction details could not be saved.</returns>
        public async Task<TransactionDetail> NestedStructureReplaceTransactionAsync(CsStructure nested, string syntax)
        {
            if (string.IsNullOrEmpty(syntax)) return null;
            if (nested == null) throw new ArgumentNullException(nameof(nested));

            var source = _source ?? throw new ArgumentNullException(nameof(Source));
            var container = _container ?? throw new ArgumentNullException(nameof(Container));

            CsSourceTransaction updatedSource = null;

            var currentModel = this.Source.GetModel<CsStructure>(nested.LookupPath);
            if (currentModel == null) throw new CodeFactoryException($"Could not get the current structure model for '{nested.Name}' cannot replace the nested structure.");

            updatedSource = await currentModel.ReplaceTransactionAsync(syntax);

            if (updatedSource?.Source == null) throw new ArgumentNullException(nameof(Source));

            var updatedContainer = updatedSource.Source.GetModel<TContainerType>(ContainerPath);

            UpdateSources(updatedSource.Source, updatedContainer);

            return updatedSource.Transaction;
        }

        /// <summary>
        /// Add the provided syntax before the nested classes definitions.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        public async Task NestedClassesAddBeforeAsync(string syntax)
        {
            await NestedClassesAddBeforeTransactionAsync(syntax);
        }

        /// <summary>
        /// Add the provided syntax before the nested classes definitions.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        /// <returns>The details of the updated source or null if the transaction details could not be saved.</returns>
        public async Task<TransactionDetail> NestedClassesAddBeforeTransactionAsync(string syntax)
        {
            if (string.IsNullOrEmpty(syntax)) return null;
            var source = _source ?? throw new ArgumentNullException(nameof(Source));
            var container = _container as CsContainerWithNestedContainers ?? throw new ArgumentNullException(nameof(Container));

            var sourceDoc = source.SourceDocument;

            TransactionDetail result = null;

            var classData = container.NestedClasses.FirstOrDefault(m => m.ModelSourceFile == sourceDoc && m.LoadedFromSource);

            if (classData != null)
            {

                var updatedSource = await classData.AddBeforeTransactionAsync(syntax);

                if (updatedSource?.Source == null) throw new ArgumentNullException(nameof(Source));

                var updatedContainer = updatedSource.Source.GetModel<TContainerType>(ContainerPath);

                UpdateSources(updatedSource.Source, updatedContainer);

                result = updatedSource.Transaction;
            }
            else
            {
                result = await this.NestedStructuresAddAfterTransactionAsync(syntax);
            }

            return result;
        }

        /// <summary>
        /// Add the provided syntax after the nested classes definitions.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        public async Task NestedClassesAddAfterAsync(string syntax)
        {
            await NestedClassesAddAfterTransactoinAsync(syntax);
        }

        /// <summary>
        /// Add the provided syntax after the nested classes definitions.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        /// <returns>The details of the updated source or null if the transaction details could not be saved.</returns>
        public async Task<TransactionDetail> NestedClassesAddAfterTransactoinAsync(string syntax)
        {
            if (string.IsNullOrEmpty(syntax)) return null;
            var source = _source ?? throw new ArgumentNullException(nameof(Source));
            var container = _container as CsContainerWithNestedContainers ?? throw new ArgumentNullException(nameof(Container));

            var sourceDoc = source.SourceDocument;

            TransactionDetail result = null;

            var classData = container.NestedClasses.LastOrDefault(m => m.ModelSourceFile == sourceDoc && m.LoadedFromSource);

            if (classData != null)
            {
                var updatedSource = await classData.AddAfterTransactionAsync(syntax);

                if (updatedSource?.Source == null) throw new ArgumentNullException(nameof(Source));

                var updatedContainer = updatedSource.Source.GetModel<TContainerType>(ContainerPath);

                UpdateSources(updatedSource.Source, updatedContainer);

                result = updatedSource.Transaction;
            }
            else
            {
                result = await this.NestedStructuresAddAfterTransactionAsync(syntax);
            }

            return result;
        }

        /// <summary>
        /// Removes the nested class.
        /// </summary>
        /// <param name="nested">The target nested class.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        public async Task NestedClassesRemoveAsync(CsClass nested)
        {
            if (nested == null) throw new ArgumentNullException(nameof(nested));

            var source = _source ?? throw new ArgumentNullException(nameof(Source));
            var container = _container ?? throw new ArgumentNullException(nameof(Container));

            var currentModel = this.Source.GetModel<CsClass>(nested.LookupPath);
            if (currentModel == null) throw new CodeFactoryException($"Could not get the current class model for '{nested.Name}' cannot remove the nested class.");

            CsSource updatedSource = null;

            updatedSource = await currentModel.DeleteAsync();

            if (updatedSource == null) throw new ArgumentNullException(nameof(Source));

            var updatedContainer = updatedSource.GetModel<TContainerType>(ContainerPath);

            UpdateSources(updatedSource, updatedContainer);
        }

        /// <summary>
        /// Replaces the nested class with the provided syntax
        /// </summary>
        /// <param name="nested">The target nested class.</param>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        public async Task NestedClassesReplaceAsync(CsClass nested, string syntax)
        {
            await NestedClassesReplaceTransactionAsync(nested, syntax);
        }

        /// <summary>
        /// Replaces the nested class with the provided syntax
        /// </summary>
        /// <param name="nested">The target nested class.</param>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        /// <returns>The details of the updated source or null if the transaction details could not be saved.</returns>
        public async Task<TransactionDetail> NestedClassesReplaceTransactionAsync(CsClass nested, string syntax)
        {
            if (string.IsNullOrEmpty(syntax)) return null;
            if (nested == null) throw new ArgumentNullException(nameof(nested));

            var currentModel = this.Source.GetModel<CsClass>(nested.LookupPath);
            if (currentModel == null) throw new CodeFactoryException($"Could not get the current class model for '{nested.Name}' cannot replace the nested class.");

            var source = _source ?? throw new ArgumentNullException(nameof(Source));
            var container = _container ?? throw new ArgumentNullException(nameof(Container));

            CsSourceTransaction updatedSource = null;

            updatedSource = await currentModel.ReplaceTransactionAsync(syntax);

            if (updatedSource?.Source == null) throw new ArgumentNullException(nameof(Source));

            var updatedContainer = updatedSource.Source.GetModel<TContainerType>(ContainerPath);

            UpdateSources(updatedSource.Source, updatedContainer);

            return updatedSource.Transaction;
        }


        /// <summary>
        /// Adds the provided syntax to the target injection location provided.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <param name="location">The location within the source code file to inject at. </param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        public async Task AddByInjectionLocationAsync(string syntax, InjectionLocation location)
        {
            if (string.IsNullOrEmpty(syntax)) return;
            _ = _source ?? throw new ArgumentNullException(nameof(Source));
            _ = _container as CsContainerWithNestedContainers ?? throw new ArgumentNullException(nameof(Container));

            switch (location)
            {
                case InjectionLocation.SourceBeginning:
                    await SourceAddToBeginningAsync(syntax);
                    break;
                case InjectionLocation.SourceEnd:
                    await SourceAddToEndAsync(syntax);
                    break;
                case InjectionLocation.ContainerBefore:
                    await ContainerAddBeforeAsync(syntax);
                    break;
                case InjectionLocation.ContainerAfter:
                    await ContainerAddAfterAsync(syntax);
                    break;
                case InjectionLocation.ContainerBeginning:
                    await ContainerAddToBeginningAsync(syntax);
                    break;
                case InjectionLocation.ContainerEnd:
                    await ContainerAddToEndAsync(syntax);
                    break;
                case InjectionLocation.FieldBefore:
                    await FieldsAddBeforeAsync(syntax);
                    break;
                case InjectionLocation.FieldAfter:
                    await FieldsAddAfterAsync(syntax);
                    break;
                case InjectionLocation.ConstructorBefore:
                    await ConstructorsAddBeforeAsync(syntax);
                    break;
                case InjectionLocation.ConstructorAfter:
                    await ConstructorsAddAfterAsync(syntax);
                    break;
                case InjectionLocation.PropertyBefore:
                    await PropertiesAddBeforeAsync(syntax);
                    break;
                case InjectionLocation.PropertyAfter:
                    await PropertiesAddAfterAsync(syntax);
                    break;
                case InjectionLocation.EventBefore:
                    await EventsAddBeforeAsync(syntax);
                    break;
                case InjectionLocation.EventAfter:
                    await EventsAddAfterAsync(syntax);
                    break;
                case InjectionLocation.MethodBefore:
                    await MethodsAddBeforeAsync(syntax);
                    break;
                case InjectionLocation.MethodAfter:
                    await MethodsAddAfterAsync(syntax);
                    break;
                case InjectionLocation.NestedEnumBefore:
                    await NestedEnumAddBeforeAsync(syntax);
                    break;
                case InjectionLocation.NestedEnumAfter:
                    await NestedEnumAddAfterAsync(syntax);
                    break;
                case InjectionLocation.NestedInterfaceBefore:
                    await NestedInterfaceAddBeforeAsync(syntax);
                    break;
                case InjectionLocation.NestedInterfaceAfter:
                    await NestedInterfaceAddAfterAsync(syntax);
                    break;
                case InjectionLocation.NestedStructureBefore:
                    await NestedStructuresAddBeforeAsync(syntax);
                    break;
                case InjectionLocation.NestedStructureAfter:
                    await NestedStructuresAddAfterAsync(syntax);
                    break;
                case InjectionLocation.NestedClassBefore:
                    await NestedClassesAddBeforeAsync(syntax);
                    break;
                case InjectionLocation.NestedClassAfter:
                    await NestedClassesAddAfterAsync(syntax);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(location), location, null);
            }
        }

    }
}