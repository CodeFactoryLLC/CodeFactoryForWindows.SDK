using System;
using System.Collections.Generic;

namespace CodeFactory.WinVs.Models.CSharp
{
    /// <summary>
    /// Provides extension methods for extracting namespaces from various C# code model elements such as methods, classes, interfaces, properties, fields, events, records, and record structs. These methods analyze the types used in these elements (e.g., return types, parameter types, base classes, implemented interfaces) and collect the namespaces they belong to. This can be useful for determining which namespaces need to be included in a source file to use these elements without fully qualifying type names.
    /// </summary>
    public static class NamespaceExtensions
    {
        /// <summary>
        /// Iteratively adds the namespace of the given <see cref="CsType"/> and all of its
        /// generic type arguments to the supplied <paramref name="namespaces"/> set.
        /// Uses an explicit <see cref="Stack{T}"/> instead of recursion to prevent
        /// stack overflow on deeply nested generic types.
        /// </summary>
        /// <param name="type">
        /// The <see cref="CsType"/> whose namespace — and the namespaces of any generic
        /// type arguments — should be collected. No-op when <see langword="null"/>.
        /// </param>
        /// <param name="namespaces">
        /// The <see cref="HashSet{T}"/> that accumulates the discovered namespace strings.
        /// Must not be <see langword="null"/>.
        /// </param>
        private static void AddTypeNamespaces(CsType type, HashSet<string> namespaces)
        {
            if (type == null) return;

            var stack = new Stack<CsType>();
            stack.Push(type);

            while (stack.Count > 0)
            {
                var current = stack.Pop();

                if (!string.IsNullOrEmpty(current.Namespace))
                    namespaces.Add(current.Namespace);

                if (current.IsGeneric && current.HasStrongTypesInGenerics && current.GenericTypes != null)
                {
                    foreach (var genericType in current.GenericTypes)
                    {
                        if (genericType != null)
                            stack.Push(genericType);
                    }
                }
            }
        }

        /// <summary>
        /// Adds the namespace of a <see cref="CsContainer"/> (class, interface, record, etc.)
        /// and the namespaces of any strongly-typed generic arguments it carries.
        /// Containers are not <see cref="CsType"/> instances, so they require their own helper.
        /// </summary>
        /// <param name="container">
        /// The <see cref="CsContainer"/> whose namespace — and the namespaces of any generic
        /// type arguments — should be collected. No-op when <see langword="null"/>.
        /// </param>
        /// <param name="namespaces">
        /// The <see cref="HashSet{T}"/> that accumulates the discovered namespace strings.
        /// Must not be <see langword="null"/>.
        /// </param>
        private static void AddContainerNamespaces(CsContainer container, HashSet<string> namespaces)
        {
            if (container == null) return;

            if (!string.IsNullOrEmpty(container.Namespace))
                namespaces.Add(container.Namespace);

            // GenericTypes on a container is IReadOnlyList<CsType> — walk each arg
            if (container.IsGeneric && container.HasStrongTypesInGenerics && container.GenericTypes != null)
            {
                foreach (var genericType in container.GenericTypes)
                    AddTypeNamespaces(genericType, namespaces);
            }
        }

        /// <summary>
        /// Adds the namespace of each attribute's type from the given
        /// <paramref name="attributes"/> collection to the supplied
        /// <paramref name="namespaces"/> set.
        /// </summary>
        /// <param name="attributes">
        /// The sequence of <see cref="CsAttribute"/> instances to inspect.
        /// No-op when <see langword="null"/>.
        /// </param>
        /// <param name="namespaces">
        /// The <see cref="HashSet{T}"/> that accumulates the discovered namespace strings.
        /// Must not be <see langword="null"/>.
        /// </param>
        private static void AddAttributeNamespaces(IEnumerable<CsAttribute> attributes, HashSet<string> namespaces)
        {
            if (attributes == null) return;

            foreach (var attr in attributes)
            {
                if (attr?.Type != null)
                    AddTypeNamespaces(attr.Type, namespaces);
            }
        }

        /// <summary>
        /// Collects namespaces used by the provided C# code model element into the supplied
        /// <paramref name="namespaces"/> set. Supported concrete types are
        /// <see cref="CsMethod"/>, <see cref="CsClass"/>, <see cref="CsInterface"/>,
        // <see cref="CsProperty"/>, <see cref="CsField"/>, <see cref="CsEvent"/>,
        // <see cref="CsRecord"/>, and <see cref="CsRecordStructure"/>.
        // Unrecognised or <see langword="null"/> sources are silently ignored.
        // </summary>
        /// <param name="source">
        /// The <see cref="ICsModel"/> element to analyze.
        /// No-op when <see langword="null"/> or an unrecognised type.
        /// </param>
        /// <param name="namespaces">
        /// The caller-supplied <see cref="HashSet{T}"/> into which discovered namespace
        /// strings are added. Must not be <see langword="null"/>.
        /// </param>
        /// <param name="includeAttributes">
        /// When <see langword="true"/>, namespaces from attribute types applied to the element
        /// (and, where applicable, to its members) are also included. Defaults to
        /// <see langword="false"/>.
        /// </param>
        public static void GetNamespaces(this ICsModel source, HashSet<string> namespaces, bool includeAttributes = false)
        {
            if (source == null || namespaces == null) return;

            switch (source)
            {
                case CsMethod method:
                    method.GetMethodNamespaces(namespaces, includeAttributes: includeAttributes);
                    break;
                case CsClass csClass:
                    csClass.GetClassNamespaces(namespaces, includeAttributes: includeAttributes);
                    break;
                case CsInterface csInterface:
                    csInterface.GetInterfaceNamespaces(namespaces, includeAttributes: includeAttributes);
                    break;
                case CsProperty csProperty:
                    csProperty.GetPropertyNamespaces(namespaces, includeAttributes: includeAttributes);
                    break;
                case CsField csField:
                    csField.GetFieldNamespaces(namespaces, includeAttributes: includeAttributes);
                    break;
                case CsEvent csEvent:
                    csEvent.GetEventNamespaces(namespaces, includeAttributes: includeAttributes);
                    break;
                case CsRecord csRecord:
                    csRecord.GetRecordNamespace(namespaces, includeAttributes: includeAttributes);
                    break;
                case CsRecordStructure csRecordStruct:
                    csRecordStruct.GetRecordStructNamespace(namespaces, includeAttributes: includeAttributes);
                    break;
                case CsStructure csStruct:
                    csStruct.GetStructureNamespace(namespaces, includeAttributes: includeAttributes);
                    break;
            }
        }

        /// <summary>
        /// Gets a list of namespaces used by the provided C# code model element, including types used in methods, classes, interfaces, properties, fields, events, records, and record structs. The method determines the type of the provided code model element and calls the appropriate method to extract the namespaces based on the specific characteristics of that element. Optionally includes namespaces from attribute types if specified.
        /// </summary>
        /// <param name="source">
        /// The <see cref="ICsModel"/> element to analyze. Supported concrete types are
        /// <see cref="CsMethod"/>, <see cref="CsClass"/>, <see cref="CsInterface"/>,
        // <see cref="CsProperty"/>, <see cref="CsField"/>, <see cref="CsEvent"/>,
        // <see cref="CsRecord"/>, and <see cref="CsRecordStructure"/>.
        // Returns an empty collection when <see langword="null"/> or an unrecognised type.
        // </param>
        /// <param name="includeAttributes">
        /// When <see langword="true"/>, namespaces from attribute types applied to the element
        /// (and, where applicable, to its members) are also included. Defaults to
        /// <see langword="false"/>.
        /// </param>
        /// <returns>
        /// A read-only collection of fully-qualified namespace strings used by the element.
        /// Never <see langword="null"/>; returns an empty collection for unrecognised types.
        /// </returns>
        public static IReadOnlyCollection<string> GetNamespaces(this ICsModel source, bool includeAttributes = false)
        {
            if (source == null) return Array.Empty<string>();

            var namespaces = new HashSet<string>(StringComparer.Ordinal);
            source.GetNamespaces(namespaces, includeAttributes: includeAttributes);
            return namespaces;
        }


        /// <summary>
        /// Collects namespaces used by the method — including the return type, parameter
        /// types, and optionally attribute types — into the supplied
        /// <paramref name="namespaces"/> set.
        /// </summary>
        /// <param name="source">
        /// The <see cref="CsMethod"/> to analyze.
        /// No-op when <see langword="null"/>.
        /// </param>
        /// <param name="namespaces">
        /// The caller-supplied <see cref="HashSet{T}"/> into which discovered namespace
        /// strings are added. Must not be <see langword="null"/>.
        /// </param>
        /// <param name="includeReturnType">
        /// When <see langword="true"/> (the default), the namespace of the method's return type
        /// is included in the result.
        /// </param>
        /// <param name="includeParameters">
        /// When <see langword="true"/> (the default), the namespace of each parameter's type
        /// is included in the result.
        /// </param>
        /// <param name="includeAttributes">
        /// When <see langword="true"/>, namespaces from attribute types applied to the method
        /// and to each of its parameters are also included. Defaults to <see langword="false"/>.
        /// </param>
        public static void GetMethodNamespaces(this CsMethod source, HashSet<string> namespaces, bool includeReturnType = true, bool includeParameters = true, bool includeAttributes = false)
        {
            if (source == null || namespaces == null) return;

            if (includeReturnType)
                AddTypeNamespaces(source.ReturnType, namespaces);

            if (includeParameters && source.Parameters != null)
            {
                foreach (var param in source.Parameters)
                {
                    AddTypeNamespaces(param.ParameterType, namespaces);

                    if (includeAttributes)
                        AddAttributeNamespaces(param.Attributes, namespaces);
                }
            }

            if (includeAttributes)
                AddAttributeNamespaces(source.Attributes, namespaces);
        }

        /// <summary>
        /// Gets a list of namespaces used by the method, including the return type, parameter types, and optionally attribute types.
        /// </summary>
        /// <param name="source">
        /// The <see cref="CsMethod"/> to analyze.
        /// Returns an empty collection when <see langword="null"/>.
        /// </param>
        /// <param name="includeReturnType">
        /// When <see langword="true"/> (the default), the namespace of the method's return type
        /// is included in the result.
        /// </param>
        /// <param name="includeParameters">
        /// When <see langword="true"/> (the default), the namespace of each parameter's type
        /// is included in the result.
        /// </param>
        /// <param name="includeAttributes">
        /// When <see langword="true"/>, namespaces from attribute types applied to the method
        /// and to each of its parameters are also included. Defaults to <see langword="false"/>.
        /// </param>
        /// <returns>
        /// A read-only collection of fully-qualified namespace strings referenced by the
        /// method's signature. Never <see langword="null"/>.
        /// </returns>
        public static IReadOnlyCollection<string> GetMethodNamespaces(this CsMethod source, bool includeReturnType = true, bool includeParameters = true, bool includeAttributes = false)
        {
            if (source == null) return Array.Empty<string>();

            var namespaces = new HashSet<string>(StringComparer.Ordinal);
            source.GetMethodNamespaces(namespaces, includeReturnType, includeParameters, includeAttributes);
            return namespaces;
        }

        /// <summary>
        /// Collects namespaces used by the class — including the base class, implemented
        /// interfaces, and optionally attribute types — into the supplied
        /// <paramref name="namespaces"/> set.
        /// </summary>
        /// <param name="source">
        /// The <see cref="CsClass"/> to analyze.
        /// No-op when <see langword="null"/>.
        /// </param>
        /// <param name="namespaces">
        /// The caller-supplied <see cref="HashSet{T}"/> into which discovered namespace
        /// strings are added. Must not be <see langword="null"/>.
        /// </param>
        /// <param name="includeBaseClasses">
        /// When <see langword="true"/> (the default), the namespace of the class's direct base
        /// class is included in the result.
        /// </param>
        /// <param name="includeInterfaces">
        /// When <see langword="true"/> (the default), the namespace of each directly implemented
        /// interface is included in the result.
        /// </param>
        /// <param name="includeAttributes">
        /// When <see langword="true"/>, namespaces from attribute types applied to the class
        /// are also included. Defaults to <see langword="false"/>.
        /// </param>
        public static void GetClassNamespaces(this CsClass source, HashSet<string> namespaces, bool includeBaseClasses = true, bool includeInterfaces = true, bool includeAttributes = false)
        {
            if (source == null || namespaces == null) return;

            // BaseClass is CsClass (a container), not CsType — use AddContainerNamespaces
            if (includeBaseClasses && source.BaseClass != null)
                AddContainerNamespaces(source.BaseClass, namespaces);

            // DirectInheritedInterfaces is IReadOnlyList<CsInterface> (containers), not CsType
            if (includeInterfaces && source.DirectInheritedInterfaces != null)
            {
                foreach (var iface in source.DirectInheritedInterfaces)
                {
                    if (iface != null)
                        AddContainerNamespaces(iface, namespaces);
                }
            }

            if (includeAttributes)
                AddAttributeNamespaces(source.Attributes, namespaces);
        }

        /// <summary>
        /// Gets a list of namespaces used by the class, including the base class, implemented interfaces, and optionally attribute types.
        /// </summary>
        /// <param name="source">
        /// The <see cref="CsClass"/> to analyze.
        /// Returns an empty collection when <see langword="null"/>.
        /// </param>
        /// <param name="includeBaseClasses">
        /// When <see langword="true"/> (the default), the namespace of the class's direct base
        /// class is included in the result.
        /// </param>
        /// <param name="includeInterfaces">
        /// When <see langword="true"/> (the default), the namespace of each directly implemented
        /// interface is included in the result.
        /// </param>
        /// <param name="includeAttributes">
        /// When <see langword="true"/>, namespaces from attribute types applied to the class
        /// are also included. Defaults to <see langword="false"/>.
        /// </param>
        /// <returns>
        /// A read-only collection of fully-qualified namespace strings referenced by the class
        /// declaration. Never <see langword="null"/>.
        /// </returns>
        public static IReadOnlyCollection<string> GetClassNamespaces(this CsClass source, bool includeBaseClasses = true, bool includeInterfaces = true, bool includeAttributes = false)
        {
            if (source == null) return Array.Empty<string>();

            var namespaces = new HashSet<string>( StringComparer.Ordinal);
            source.GetClassNamespaces(namespaces, includeBaseClasses, includeInterfaces, includeAttributes);
            return namespaces;
        }

        /// <summary>
        /// Collects namespaces used by the interface — including base interfaces and
        /// optionally attribute types — into the supplied <paramref name="namespaces"/> set.
        /// </summary>
        /// <param name="source">
        /// The <see cref="CsInterface"/> to analyze.
        /// No-op when <see langword="null"/>.
        /// </param>
        /// <param name="namespaces">
        /// The caller-supplied <see cref="HashSet{T}"/> into which discovered namespace
        /// strings are added. Must not be <see langword="null"/>.
        /// </param>
        /// <param name="includeBaseInterfaces">
        /// When <see langword="true"/> (the default), the namespace of each directly inherited
        /// base interface is included in the result.
        /// </param>
        /// <param name="includeAttributes">
        /// When <see langword="true"/>, namespaces from attribute types applied to the interface
        /// are also included. Defaults to <see langword="false"/>.
        /// </param>
        public static void GetInterfaceNamespaces(this CsInterface source, HashSet<string> namespaces, bool includeBaseInterfaces = true, bool includeAttributes = false)
        {
            if (source == null || namespaces == null) return;

            // DirectInheritedInterfaces is IReadOnlyList<CsInterface> (containers), not CsType
            if (includeBaseInterfaces && source.DirectInheritedInterfaces != null)
            {
                foreach (var iface in source.DirectInheritedInterfaces)
                {
                    if (iface != null)
                        AddContainerNamespaces(iface, namespaces);
                }
            }

            if (includeAttributes)
                AddAttributeNamespaces(source.Attributes, namespaces);
        }

        /// <summary>
        /// Gets a list of namespaces used by the interface, including the base interfaces and optionally attribute types.
        /// </summary>
        /// <param name="source">
        /// The <see cref="CsInterface"/> to analyze.
        /// Returns an empty collection when <see langword="null"/>.
        /// </param>
        /// <param name="includeBaseInterfaces">
        /// When <see langword="true"/> (the default), the namespace of each directly inherited
        /// base interface is included in the result.
        /// </param>
        /// <param name="includeAttributes">
        /// When <see langword="true"/>, namespaces from attribute types applied to the interface
        /// are also included. Defaults to <see langword="false"/>.
        /// </param>
        /// <returns>
        /// A read-only collection of fully-qualified namespace strings referenced by the
        /// interface declaration. Never <see langword="null"/>.
        /// </returns>
        public static IReadOnlyCollection<string> GetInterfaceNamespaces(this CsInterface source, bool includeBaseInterfaces = true, bool includeAttributes = false)
        {
            if (source == null) return Array.Empty<string>();

            var namespaces = new HashSet<string>(StringComparer.Ordinal);
            source.GetInterfaceNamespaces(namespaces, includeBaseInterfaces, includeAttributes);
            return namespaces;
        }

        /// <summary>
        /// Collects namespaces used by the property — including the property type and
        /// optionally attribute types — into the supplied <paramref name="namespaces"/> set.
        /// </summary>
        /// <param name="source">
        /// The <see cref="CsProperty"/> to analyze.
        /// No-op when <see langword="null"/>.
        /// </param>
        /// <param name="namespaces">
        /// The caller-supplied <see cref="HashSet{T}"/> into which discovered namespace
        /// strings are added. Must not be <see langword="null"/>.
        /// </param>
        /// <param name="includePropertyType">
        /// When <see langword="true"/> (the default), the namespace of the property's declared
        /// type is included in the result.
        /// </param>
        /// <param name="includeAttributes">
        /// When <see langword="true"/>, namespaces from attribute types applied to the property
        /// are also included. Defaults to <see langword="false"/>.
        /// </param>
        public static void GetPropertyNamespaces(this CsProperty source, HashSet<string> namespaces, bool includePropertyType = true, bool includeAttributes = false)
        {
            if (source == null || namespaces == null) return;

            if (includePropertyType)
                AddTypeNamespaces(source.PropertyType, namespaces);

            if (includeAttributes)
                AddAttributeNamespaces(source.Attributes, namespaces);
        }

        /// <summary>
        /// Gets a list of namespaces used by the property, including the property type and optionally attribute types.
        /// </summary>
        /// <param name="source">
        /// The <see cref="CsProperty"/> to analyze.
        /// Returns an empty collection when <see langword="null"/>.
        /// </param>
        /// <param name="includePropertyType">
        /// When <see langword="true"/> (the default), the namespace of the property's declared
        /// type is included in the result.
        /// </param>
        /// <param name="includeAttributes">
        /// When <see langword="true"/>, namespaces from attribute types applied to the property
        /// are also included. Defaults to <see langword="false"/>.
        /// </param>
        /// <returns>
        /// A read-only collection of fully-qualified namespace strings referenced by the
        /// property declaration. Never <see langword="null"/>.
        /// </returns>
        public static IReadOnlyCollection<string> GetPropertyNamespaces(this CsProperty source, bool includePropertyType = true, bool includeAttributes = false)
        {
            if (source == null) return Array.Empty<string>();

            var namespaces = new HashSet<string>(StringComparer.Ordinal);
            source.GetPropertyNamespaces(namespaces, includePropertyType, includeAttributes);
            return namespaces;
        }

        /// <summary>
        /// Collects namespaces used by the field — including the field type and optionally
        /// attribute types — into the supplied <paramref name="namespaces"/> set.
        /// </summary>
        /// <param name="source">
        /// The <see cref="CsField"/> to analyze.
        /// No-op when <see langword="null"/>.
        /// </param>
        /// <param name="namespaces">
        /// The caller-supplied <see cref="HashSet{T}"/> into which discovered namespace
        /// strings are added. Must not be <see langword="null"/>.
        /// </param>
        /// <param name="includeFieldType">
        /// When <see langword="true"/> (the default), the namespace of the field's declared
        /// type is included in the result.
        /// </param>
        /// <param name="includeAttributes">
        /// When <see langword="true"/>, namespaces from attribute types applied to the field
        /// are also included. Defaults to <see langword="false"/>.
        /// </param>
        public static void GetFieldNamespaces(this CsField source, HashSet<string> namespaces, bool includeFieldType = true, bool includeAttributes = false)
        {
            if (source == null || namespaces == null) return;

            if (includeFieldType)
                AddTypeNamespaces(source.DataType, namespaces);

            if (includeAttributes)
                AddAttributeNamespaces(source.Attributes, namespaces);
        }

        /// <summary>
        /// Gets a list of namespaces used by the field, including the field type and optionally attribute types.
        /// </summary>
        /// <param name="source">
        /// The <see cref="CsField"/> to analyze.
        /// Returns an empty collection when <see langword="null"/>.
        /// </param>
        /// <param name="includeFieldType">
        /// When <see langword="true"/> (the default), the namespace of the field's declared
        /// type is included in the result.
        /// </param>
        /// <param name="includeAttributes">
        /// When <see langword="true"/>, namespaces from attribute types applied to the field
        /// are also included. Defaults to <see langword="false"/>.
        /// </param>
        /// <returns>
        /// A read-only collection of fully-qualified namespace strings referenced by the field
        /// declaration. Never <see langword="null"/>.
        /// </returns>
        public static IReadOnlyCollection<string> GetFieldNamespaces(this CsField source, bool includeFieldType = true, bool includeAttributes = false)
        {
            if (source == null) return Array.Empty<string>();

            var namespaces = new HashSet<string>(StringComparer.Ordinal);
            source.GetFieldNamespaces(namespaces, includeFieldType, includeAttributes);
            return namespaces;
        }

        /// <summary>
        /// Collects namespaces used by the event — including the delegate type and optionally
        /// attribute types — into the supplied <paramref name="namespaces"/> set.
        /// </summary>
        /// <param name="source">
        /// The <see cref="CsEvent"/> to analyze.
        /// No-op when <see langword="null"/>.
        /// </param>
        /// <param name="namespaces">
        /// The caller-supplied <see cref="HashSet{T}"/> into which discovered namespace
        /// strings are added. Must not be <see langword="null"/>.
        /// </param>
        /// <param name="includeEventType">
        /// When <see langword="true"/> (the default), the namespace of the event's delegate
        /// type is included in the result.
        /// </param>
        /// <param name="includeAttributes">
        /// When <see langword="true"/>, namespaces from attribute types applied to the event
        /// are also included. Defaults to <see langword="false"/>.
        /// </param>
        public static void GetEventNamespaces(this CsEvent source, HashSet<string> namespaces, bool includeEventType = true, bool includeAttributes = false)
        {
            if (source == null || namespaces == null) return;

            if (includeEventType)
                AddTypeNamespaces(source.EventType, namespaces);

            if (includeAttributes)
                AddAttributeNamespaces(source.Attributes, namespaces);
        }

        /// <summary>
        /// Gets a list of namespaces used by the event, including the event type and optionally attribute types.
        /// </summary>
        /// <param name="source">
        /// The <see cref="CsEvent"/> to analyze.
        /// Returns an empty collection when <see langword="null"/>.
        /// </param>
        /// <param name="includeEventType">
        /// When <see langword="true"/> (the default), the namespace of the event's delegate
        /// type is included in the result.
        /// </param>
        /// <param name="includeAttributes">
        /// When <see langword="true"/>, namespaces from attribute types applied to the event
        /// are also included. Defaults to <see langword="false"/>.
        /// </param>
        /// <returns>
        /// A read-only collection of fully-qualified namespace strings referenced by the event
        /// declaration. Never <see langword="null"/>.
        /// </returns>
        public static IReadOnlyCollection<string> GetEventNamespaces(this CsEvent source, bool includeEventType = true, bool includeAttributes = false)
        {
            if (source == null) return Array.Empty<string>();

            var namespaces = new HashSet<string>( StringComparer.Ordinal);
            source.GetEventNamespaces(namespaces, includeEventType, includeAttributes);
            return namespaces;
        }

        /// <summary>
        /// Collects namespaces used by the record — including the base record, implemented
        /// interfaces, and optionally attribute types — into the supplied
        /// <paramref name="namespaces"/> set.
        /// </summary>
        /// <param name="source">
        /// The <see cref="CsRecord"/> to analyze.
        /// No-op when <see langword="null"/>.
        /// </param>
        /// <param name="namespaces">
        /// The caller-supplied <see cref="HashSet{T}"/> into which discovered namespace
        /// strings are added. Must not be <see langword="null"/>.
        /// </param>
        /// <param name="includeBaseRecords">
        /// When <see langword="true"/> (the default), the namespace of the record's direct base
        /// record is included in the result.
        /// </param>
        /// <param name="includeInterfaces">
        /// When <see langword="true"/> (the default), the namespace of each directly implemented
        /// interface is included in the result.
        /// </param>
        /// <param name="includeAttributes">
        /// When <see langword="true"/>, namespaces from attribute types applied to the record
        /// are also included. Defaults to <see langword="false"/>.
        /// </param>
        public static void GetRecordNamespace(this CsRecord source, HashSet<string> namespaces, bool includeBaseRecords = true, bool includeInterfaces = true, bool includeAttributes = false)
        {
            if (source == null || namespaces == null) return;

            // BaseRecord is CsClass (a container), not CsType — use AddContainerNamespaces
            if (includeBaseRecords && source.BaseRecord != null)
                AddContainerNamespaces(source.BaseRecord, namespaces);

            // DirectInheritedInterfaces is IReadOnlyList<CsInterface> (containers), not CsType
            if (includeInterfaces && source.DirectInheritedInterfaces != null)
            {
                foreach (var iface in source.DirectInheritedInterfaces)
                {
                    if (iface != null)
                        AddContainerNamespaces(iface, namespaces);
                }
            }

            if (includeAttributes)
                AddAttributeNamespaces(source.Attributes, namespaces);
        }

        /// <summary>
        /// Gets a list of namespaces used by the record, including the base record, implemented interfaces, and optionally attribute types.
        /// </summary>
        /// <param name="source">
        /// The <see cref="CsRecord"/> to analyze.
        /// Returns an empty collection when <see langword="null"/>.
        /// </param>
        /// <param name="includeBaseRecords">
        /// When <see langword="true"/> (the default), the namespace of the record's direct base
        /// record is included in the result.
        /// </param>
        /// <param name="includeInterfaces">
        /// When <see langword="true"/> (the default), the namespace of each directly implemented
        /// interface is included in the result.
        /// </param>
        /// <param name="includeAttributes">
        /// When <see langword="true"/>, namespaces from attribute types applied to the record
        /// are also included. Defaults to <see langword="false"/>.
        /// </param>
        /// <returns>
        /// A read-only collection of fully-qualified namespace strings referenced by the record
        /// declaration. Never <see langword="null"/>.
        /// </returns>
        public static IReadOnlyCollection<string> GetRecordNamespace(this CsRecord source, bool includeBaseRecords = true, bool includeInterfaces = true, bool includeAttributes = false)
        {
            if (source == null) return Array.Empty<string>();

            var namespaces = new HashSet<string>(StringComparer.Ordinal);
            source.GetRecordNamespace(namespaces, includeBaseRecords, includeInterfaces, includeAttributes);
            return namespaces;
        }

        /// <summary>
        /// Collects namespaces used by the record struct — including implemented interfaces
        /// and optionally attribute types — into the supplied <paramref name="namespaces"/> set.
        /// </summary>
        /// <param name="source">
        /// The <see cref="CsRecordStructure"/> to analyze.
        /// No-op when <see langword="null"/>.
        /// </param>
        /// <param name="namespaces">
        /// The caller-supplied <see cref="HashSet{T}"/> into which discovered namespace
        /// strings are added. Must not be <see langword="null"/>.
        /// </param>
        /// <param name="includeInterfaces">
        /// When <see langword="true"/> (the default), the namespace of each directly implemented
        /// interface is included in the result.
        /// </param>
        /// <param name="includeAttributes">
        /// When <see langword="true"/>, namespaces from attribute types applied to the record
        /// struct are also included. Defaults to <see langword="false"/>.
        /// </param>
        public static void GetRecordStructNamespace(this CsRecordStructure source, HashSet<string> namespaces, bool includeInterfaces = true, bool includeAttributes = false)
        {
            if (source == null || namespaces == null) return;

            // DirectInheritedInterfaces is IReadOnlyList<CsInterface> (containers), not CsType
            if (includeInterfaces && source.DirectInheritedInterfaces != null)
            {
                foreach (var iface in source.DirectInheritedInterfaces)
                {
                    if (iface != null)
                        AddContainerNamespaces(iface, namespaces);
                }
            }

            if (includeAttributes)
                AddAttributeNamespaces(source.Attributes, namespaces);
        }

        /// <summary>
        /// Gets a list of namespaces used by the record struct, including implemented interfaces and optionally attribute types.
        /// </summary>
        /// <param name="source">
        /// The <see cref="CsRecordStructure"/> to analyze.
        /// Returns an empty collection when <see langword="null"/>.
        /// </param>
        /// <param name="includeInterfaces">
        /// When <see langword="true"/> (the default), the namespace of each directly implemented
        /// interface is included in the result.
        /// </param>
        /// <param name="includeAttributes">
        /// When <see langword="true"/>, namespaces from attribute types applied to the record
        /// struct are also included. Defaults to <see langword="false"/>.
        /// </param>
        /// <returns>
        /// A read-only collection of fully-qualified namespace strings referenced by the record
        /// struct declaration. Never <see langword="null"/>.
        /// </returns>
        public static IReadOnlyCollection<string> GetRecordStructNamespace(this CsRecordStructure source, bool includeInterfaces = true, bool includeAttributes = false)
        {
            if (source == null) return Array.Empty<string>();

            var namespaces = new HashSet<string>(StringComparer.Ordinal);
            source.GetRecordStructNamespace(namespaces, includeInterfaces, includeAttributes);
            return namespaces;
        }

        /// <summary>
        /// Collects namespaces used by the structure — including implemented interfaces
        /// and optionally attribute types — into the supplied <paramref name="namespaces"/> set.
        /// </summary>
        /// <param name="source">
        /// The <see cref="CsStructure"/> to analyze.
        /// No-op when <see langword="null"/>.
        /// </param>
        /// <param name="namespaces">
        /// The caller-supplied <see cref="HashSet{T}"/> into which discovered namespace
        /// strings are added. Must not be <see langword="null"/>.
        /// </param>
        /// <param name="includeInterfaces">
        /// When <see langword="true"/> (the default), the namespace of each directly implemented
        /// interface is included in the result.
        /// </param>
        /// <param name="includeAttributes">
        /// When <see langword="true"/>, namespaces from attribute types applied to the record
        /// struct are also included. Defaults to <see langword="false"/>.
        /// </param>
        public static void GetStructureNamespace(this CsStructure source, HashSet<string> namespaces, bool includeInterfaces = true, bool includeAttributes = false)
        {
            if (source == null || namespaces == null) return;

            // DirectInheritedInterfaces is IReadOnlyList<CsInterface> (containers), not CsType
            if (includeInterfaces && source.DirectInheritedInterfaces != null)
            {
                foreach (var iface in source.DirectInheritedInterfaces)
                {
                    if (iface != null)
                        AddContainerNamespaces(iface, namespaces);
                }
            }

            if (includeAttributes)
                AddAttributeNamespaces(source.Attributes, namespaces);
        }

        /// <summary>
        /// Gets a list of namespaces used by the structure, including implemented interfaces and optionally attribute types.
        /// </summary>
        /// <param name="source">
        /// The <see cref="CsStructure"/> to analyze.
        /// Returns an empty collection when <see langword="null"/>.
        /// </param>
        /// <param name="includeInterfaces">
        /// When <see langword="true"/> (the default), the namespace of each directly implemented
        /// interface is included in the result.
        /// </param>
        /// <param name="includeAttributes">
        /// When <see langword="true"/>, namespaces from attribute types applied to the record
        /// struct are also included. Defaults to <see langword="false"/>.
        /// </param>
        /// <returns>
        /// A read-only collection of fully-qualified namespace strings referenced by the record
        /// struct declaration. Never <see langword="null"/>.
        /// </returns>
        public static IReadOnlyCollection<string> GetStructureNamespace(this CsStructure source, bool includeInterfaces = true, bool includeAttributes = false)
        {
            if (source == null) return Array.Empty<string>();
            var namespaces = new HashSet<string>(StringComparer.Ordinal);
            source.GetStructureNamespace(namespaces, includeInterfaces, includeAttributes);
            return namespaces;
        }
    }
}
