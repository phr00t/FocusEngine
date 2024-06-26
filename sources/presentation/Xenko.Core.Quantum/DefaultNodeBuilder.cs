// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Xenko.Core.Annotations;
using Xenko.Core.Reflection;
using Xenko.Core.Quantum.References;

namespace Xenko.Core.Quantum
{
    /// <summary>
    /// The default <see cref="INodeBuilder"/> implementation that construct a graph from a data object.
    /// </summary>
    internal class DefaultNodeBuilder : DataVisitorBase, INodeBuilder
    {
        private readonly Stack<IInitializingGraphNode> contextStack = new Stack<IInitializingGraphNode>();
        private readonly HashSet<IGraphNode> referenceContents = new HashSet<IGraphNode>();
        private readonly List<Type> primitiveTypes = new List<Type>();
        private static readonly Type[] InternalPrimitiveTypes = { typeof(decimal), typeof(string), typeof(Guid) };
        private IInitializingObjectNode rootNode;
        private Guid rootGuid;

        public DefaultNodeBuilder(NodeContainer nodeContainer)
        {
            NodeContainer = nodeContainer;
            primitiveTypes.AddRange(InternalPrimitiveTypes);
        }

        /// <inheritdoc/>
        public NodeContainer NodeContainer { get; }

        /// <inheritdoc/>
        public INodeFactory NodeFactory { get; set; } = new DefaultNodeFactory();

        /// <summary>
        /// Reset the visitor in order to use it to generate another model.
        /// </summary>
        public override void Reset()
        {
            rootNode = null;
            rootGuid = Guid.Empty;
            contextStack.Clear();
            referenceContents.Clear();
            base.Reset();
        }

        public void RegisterPrimitiveType([NotNull] Type type)
        {
            if (type.IsPrimitive || type.IsEnum || primitiveTypes.Contains(type))
                return;

            primitiveTypes.Add(type);
        }

        public void UnregisterPrimitiveType([NotNull] Type type)
        {
            if (type.IsPrimitive || type.IsEnum || InternalPrimitiveTypes.Contains(type))
                throw new InvalidOperationException("The given type cannot be unregistered from the list of primitive types");

            primitiveTypes.Remove(type);
        }

        public bool IsPrimitiveType(Type type)
        {
            if (type == null)
                return false;

            var underlyingType = Nullable.GetUnderlyingType(type);
            if (underlyingType != null)
                type = underlyingType;

            return type.IsPrimitive || type.IsEnum || primitiveTypes.Any(x => x.IsAssignableFrom(type));
        }

        /// <inheritdoc/>
        public IObjectNode Build([NotNull] object obj, Guid guid)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            Reset();
            rootGuid = guid;
            var typeDescriptor = TypeDescriptorFactory.Find(obj.GetType());
            VisitObject(obj, typeDescriptor as ObjectDescriptor, true);
            return rootNode;
        }

        public override void VisitArray(Array array, ArrayDescriptor descriptor)
        {
            if (!IsPrimitiveType(descriptor.ElementType))
            {
                base.VisitArray(array, descriptor);
            }
        }

        /// <inheritdoc/>
        public override void VisitObject(object obj, ObjectDescriptor descriptor, bool visitMembers)
        {
            ITypeDescriptor currentDescriptor = descriptor;

            bool isRootNode = contextStack.Count == 0;
            if (isRootNode)
            {
                // If we're visiting a value type as "object" we need to use a special "boxed" node.
                var content = descriptor.Type.IsValueType ? NodeFactory.CreateBoxedNode(this, rootGuid, obj, descriptor)
                    : NodeFactory.CreateObjectNode(this, rootGuid, obj, descriptor);

                currentDescriptor = content.Descriptor;
                rootNode = (IInitializingObjectNode)content;
                if (content.IsReference && currentDescriptor.Type.IsStruct())
                    throw new QuantumConsistencyException("A collection type", "A structure type", rootNode);

                if (content.IsReference)
                    referenceContents.Add(content);

                PushContextNode(rootNode);
            }

            if (!IsPrimitiveType(currentDescriptor.Type))
            {
                base.VisitObject(obj, descriptor, true);
            }

            if (isRootNode)
            {
                PopContextNode();
                rootNode.Seal();
            }
        }

        /// <inheritdoc/>
        public override void VisitCollection(IEnumerable collection, CollectionDescriptor descriptor)
        {
            if (!descriptor.HasIndexerAccessors)
                return; // HashSet or some other set, skip this (don't crash loading the project though, geez!)

            // Don't visit items unless they are primitive or enumerable (collections within collections)
            if (IsCollection(descriptor.ElementType))
            {
                base.VisitCollection(collection, descriptor);
            }
        }

        /// <inheritdoc/>
        public override void VisitDictionary(object dictionary, DictionaryDescriptor descriptor)
        {
            if (!IsPrimitiveType(descriptor.KeyType))
                return; // unsupported dictionary, skip this (don't crash loading the project though, geez!)

            // Don't visit items unless they are primitive or enumerable (collections within collections)
            if (IsCollection(descriptor.ValueType))
            {
                base.VisitDictionary(dictionary, descriptor);
            }
        }

        /// <inheritdoc/>
        public override void VisitObjectMember(object container, ObjectDescriptor containerDescriptor, IMemberDescriptor member, object value)
        {
            // If this member should contains a reference, create it now.
            var containerNode = (IInitializingObjectNode)GetContextNode();
            var guid = Guid.NewGuid();
            var content = (MemberNode)NodeFactory.CreateMemberNode(this, guid, containerNode, member, value);
            containerNode.AddMember(content);

            if (content.IsReference)
                referenceContents.Add(content);

            PushContextNode(content);
            if (content.TargetReference == null)
            {
                // For enumerable references, we visit the member to allow VisitList or VisitDictionary or VisitSet or VisitCollection to enrich correctly the node.
                Visit(content.Retrieve());
            }
            PopContextNode();

            content.Seal();
        }

        [CanBeNull]
        public IReference CreateReferenceForNode(Type type, object value, bool isMember)
        {
            if (isMember)
            {
                return !IsPrimitiveType(type) ? Reference.CreateReference(value, type, NodeIndex.Empty, true) : null;
            }

            var descriptor = TypeDescriptorFactory.Find(value?.GetType());
            if (descriptor is CollectionDescriptor || descriptor is DictionaryDescriptor)
            {
                var valueType = GetElementValueType(descriptor);
                return !IsPrimitiveType(valueType) ? Reference.CreateReference(value, type, NodeIndex.Empty, false) : null;
            }

            return null;
        }

        private void PushContextNode(IInitializingGraphNode node)
        {
            contextStack.Push(node);
        }

        private void PopContextNode()
        {
            contextStack.Pop();
        }

        private IInitializingGraphNode GetContextNode()
        {
            return contextStack.Peek();
        }

        private static bool IsCollection(Type type)
        {
            return typeof(ICollection).IsAssignableFrom(type);
        }

        [CanBeNull]
        private static Type GetElementValueType(ITypeDescriptor descriptor)
        {
            var dictionaryDescriptor = descriptor as DictionaryDescriptor;
            var collectionDescriptor = descriptor as CollectionDescriptor;
            return dictionaryDescriptor != null ? dictionaryDescriptor.ValueType : collectionDescriptor?.ElementType;
        }
    }
}
