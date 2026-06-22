using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CodeFactory.WinVs.Stats;

namespace CodeFactory.WinVs.Models.CSharp.Builder
{
    /// <summary>
    /// Manages changes to a hosting <see cref="CsSource"/> model and the target <see cref="CsInterface"/> model hosted in source code.
    /// </summary>
    public class SourceInterfaceManager : SourceContainerManager<CsInterface>
    {
        /// <summary>
        /// Constructor for the source interface manager.
        /// </summary>
        /// <param name="source">The C# source code to be managed.</param>
        /// <param name="container">The target interface to be managed.</param>
        /// <param name="vsActions">The CodeFactory API for Visual Studio.</param>
        /// <param name="namespaceManager">Optional parameter that sets the default namespace manager to use, default is null.</param>
        /// <param name="mappedNamespaces">Optional parameter that sets the mapped namespaces used for namespace management.</param>
        public SourceInterfaceManager(CsSource source, CsInterface container, IVsActions vsActions, NamespaceManager namespaceManager = null, List<MapNamespace> mappedNamespaces = null) : base(source, container, vsActions, namespaceManager, mappedNamespaces)
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
        public override Task<TransactionDetail> FieldsAddBeforeTransactionAsync(string syntax)
        {
            if (string.IsNullOrEmpty(syntax)) return null;
            return ContainerAddToBeginningTransactionAsync(syntax);
        }

        /// <summary>
        /// Adds the provided syntax after the field definitions.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
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
        public override Task<TransactionDetail> FieldsAddAfterTransactionAsync(string syntax)
        {
            if (string.IsNullOrEmpty(syntax)) return null;
            return ContainerAddToBeginningTransactionAsync(syntax);
        }

        /// <summary>
        /// Add the provided syntax before the constructor definitions.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
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
        public override Task<TransactionDetail> ConstructorsAddBeforeTransactionAsync(string syntax)
        {
            if (string.IsNullOrEmpty(syntax)) return null;
            return ContainerAddToBeginningTransactionAsync(syntax);
        }

        /// <summary>
        /// Add the provided syntax after the constructor definitions.
        /// </summary>
        /// <param name="syntax">Target syntax to be added.</param>
        /// <exception cref="ArgumentNullException">Thrown if either the source or the container is null after updating.</exception>
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
        public override Task<TransactionDetail> ConstructorsAddAfterTransactionAsync(string syntax)
        {
            if (string.IsNullOrEmpty(syntax)) return null;
            return ContainerAddToBeginningTransactionAsync(syntax);
        }
    }
}

