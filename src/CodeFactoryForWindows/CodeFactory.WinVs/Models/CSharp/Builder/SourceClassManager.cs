using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CodeFactory.WinVs.Stats;

namespace CodeFactory.WinVs.Models.CSharp.Builder
{
    /// <summary>
    /// Manages changes to a hosting <see cref="CsSource"/> model and the target <see cref="CsClass"/> model hosted in source code.
    /// </summary>
    public class SourceClassManager : SourceContainerManager<CsClass>
    {
        /// <summary>
        /// Constructor for the source class manager.
        /// </summary>
        /// <param name="source">The C# source code to be managed.</param>
        /// <param name="container">The target class to be managed.</param>
        /// <param name="vsActions">The CodeFactory API for Visual Studio.</param>
        /// <param name="namespaceManager">Optional parameter that sets the default namespace manager to use, default is null.</param>
        /// <param name="mappedNamespaces">Optional parameter that sets the mapped namespaces used for namespace management.</param>
        public SourceClassManager(CsSource source, CsClass container, IVsActions vsActions, NamespaceManager namespaceManager = null, List<MapNamespace> mappedNamespaces = null) : base(source, container, vsActions, namespaceManager, mappedNamespaces)
        {
            //Intentionally blank
        }

        /// <summary>
        /// Checks all types definitions for the loaded container if the container is not loaded will not add missing using statements.
        /// </summary>
        [Obsolete("Use AddNamespacesFromContainerAsync instead. This method will be removed in a future version.")]
        public override Task AddMissingUsingStatementsAsync()
        {
            //Calling the base implementation
            return base.AddNamespacesFromContainerAsync();
        }

        /// <summary>
        /// Adds the provided syntax before the field definitions.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        // PERF: No async state machine — directly returns the Task from the transaction method.
        public override Task FieldsAddBeforeAsync(string syntax)
        {
            return FieldsAddBeforeTransactionAsync(syntax);
        }

        /// <summary>
        /// Adds the provided syntax before the field definitions.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        /// <returns>The details of the updated source or null if the transaction details could not be saved.</returns>
        public override async Task<TransactionDetail> FieldsAddBeforeTransactionAsync(string syntax)
        {
            if (string.IsNullOrEmpty(syntax)) return null;
            var source = Source ?? throw new ArgumentNullException(nameof(Source));
            var container = Container ?? throw new ArgumentNullException(nameof(Container));

            var sourceDoc = source.SourceDocument;

            // PERF: Single-pass — FirstOrDefault replaces Any() + First() (was 2 iterations).
            var fieldData = container.Fields.FirstOrDefault(f => f.ModelSourceFile == sourceDoc && f.LoadedFromSource);

            if (fieldData != null)
            {
                var updatedSource = await fieldData.AddBeforeTransactionAsync(syntax);

                if (updatedSource?.Source == null) throw new ArgumentNullException(nameof(Source));

                var updatedContainer = updatedSource.Source.GetModel<CsClass>(ContainerPath);

                UpdateSources(updatedSource.Source, updatedContainer);

                return updatedSource.Transaction;
            }

            return await ContainerAddToBeginningTransactionAsync(syntax);
        }

        /// <summary>
        /// Adds the provided syntax after the field definitions.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        // PERF: No async state machine — directly returns the Task from the transaction method.
        public override Task FieldsAddAfterAsync(string syntax)
        {
            return FieldsAddAfterTransactionAsync(syntax);
        }

        /// <summary>
        /// Adds the provided syntax after the field definitions.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        /// <returns>The details of the updated source or null if the transaction details could not be saved.</returns>
        public override async Task<TransactionDetail> FieldsAddAfterTransactionAsync(string syntax)
        {
            if (string.IsNullOrEmpty(syntax)) return null;
            var source = Source ?? throw new ArgumentNullException(nameof(Source));
            var container = Container ?? throw new ArgumentNullException(nameof(Container));

            var sourceDoc = source.SourceDocument;

            // PERF: Single-pass — LastOrDefault replaces Any() + Last() (was 2 iterations).
            var fieldData = container.Fields.LastOrDefault(f => f.ModelSourceFile == sourceDoc && f.LoadedFromSource);

            if (fieldData != null)
            {
                var updatedSource = await fieldData.AddAfterTransactionAsync(syntax);

                if (updatedSource?.Source == null) throw new ArgumentNullException(nameof(Source));

                var updatedContainer = updatedSource.Source.GetModel<CsClass>(ContainerPath);

                UpdateSources(updatedSource.Source, updatedContainer);

                return updatedSource.Transaction;
            }

            return await ContainerAddToBeginningTransactionAsync(syntax);
        }

        /// <summary>
        /// Add the provided syntax before the constructor definitions.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        // PERF: No async state machine — directly returns the Task from the transaction method.
        public override Task ConstructorsAddBeforeAsync(string syntax)
        {
            return ConstructorsAddBeforeTransactionAsync(syntax);
        }

        /// <summary>
        /// Add the provided syntax before the constructor definitions.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        /// <returns>The details of the updated source or null if the transaction details could not be saved.</returns>
        public override async Task<TransactionDetail> ConstructorsAddBeforeTransactionAsync(string syntax)
        {
            if (string.IsNullOrEmpty(syntax)) return null;
            var source = Source ?? throw new ArgumentNullException(nameof(Source));
            var container = Container ?? throw new ArgumentNullException(nameof(Container));

            var sourceDoc = source.SourceDocument;

            // PERF: Single-pass — FirstOrDefault replaces Any() + First() (was 2 iterations).
            var constData = container.Constructors.FirstOrDefault(c => c.ModelSourceFile == sourceDoc && c.LoadedFromSource);

            if (constData != null)
            {
                var updatedSource = await constData.AddBeforeTransactionAsync(syntax);

                if (updatedSource?.Source == null) throw new ArgumentNullException(nameof(Source));

                var updatedContainer = updatedSource.Source.GetModel<CsClass>(ContainerPath);

                UpdateSources(updatedSource.Source, updatedContainer);

                return updatedSource.Transaction;
            }

            // No constructors exist in this source file — fall back to placing syntax after
            // field definitions, which is the nearest logical position before constructors.
            return await FieldsAddAfterTransactionAsync(syntax);
        }

        /// <summary>
        /// Add the provided syntax after the constructor definitions.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        // PERF: No async state machine — directly returns the Task from the transaction method.
        public override Task ConstructorsAddAfterAsync(string syntax)
        {
            return ConstructorsAddAfterTransactionAsync(syntax);
        }

        /// <summary>
        /// Add the provided syntax after the constructor definitions.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
        /// <returns>The details of the updated source or null if the transaction details could not be saved.</returns>
        public override async Task<TransactionDetail> ConstructorsAddAfterTransactionAsync(string syntax)
        {
            if (string.IsNullOrEmpty(syntax)) return null;
            var source = Source ?? throw new ArgumentNullException(nameof(Source));
            var container = Container ?? throw new ArgumentNullException(nameof(Container));

            var sourceDoc = source.SourceDocument;

            // PERF: Single-pass — LastOrDefault replaces Any() + Last() (was 2 iterations).
            var constData = container.Constructors.LastOrDefault(c => c.ModelSourceFile == sourceDoc && c.LoadedFromSource);

            if (constData != null)
            {
                var updatedSource = await constData.AddAfterTransactionAsync(syntax);

                if (updatedSource?.Source == null) throw new ArgumentNullException(nameof(Source));

                var updatedContainer = updatedSource.Source.GetModel<CsClass>(ContainerPath);

                UpdateSources(updatedSource.Source, updatedContainer);

                return updatedSource.Transaction;
            }

            // No constructors exist in this source file — fall back to placing syntax after
            // field definitions as the best available insertion point.
            return await FieldsAddAfterTransactionAsync(syntax);
        }
    }
}
