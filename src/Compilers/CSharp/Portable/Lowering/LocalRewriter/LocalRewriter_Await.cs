﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.CodeAnalysis.CSharp.Symbols;

namespace Microsoft.CodeAnalysis.CSharp
{
    internal sealed partial class LocalRewriter
    {
        public override BoundNode VisitAwaitExpression(BoundAwaitExpression node)
        {
            return VisitAwaitExpression(node, true);
        }

        public BoundExpression VisitAwaitExpression(BoundAwaitExpression node, bool used)
        {
            return RewriteAwaitExpression((BoundAwaitExpression)base.VisitAwaitExpression(node)!, used);
        }

        private BoundExpression RewriteAwaitExpression(SyntaxNode syntax, BoundExpression rewrittenExpression, BoundAwaitableInfo awaitableInfo, TypeSymbol type, bool used)
        {
            return RewriteAwaitExpression(new BoundAwaitExpression(syntax, false, rewrittenExpression, awaitableInfo, type) { WasCompilerGenerated = true }, used);
        }

        /// <summary>
        /// Lower an await expression that has already had its components rewritten.
        /// </summary>
        private BoundExpression RewriteAwaitExpression(BoundAwaitExpression rewrittenAwait, bool used)
        {
            _sawAwait = true;

            if (!used && !rewrittenAwait.IsConditional)
            {
                // Await expression is already at the statement level.
                return rewrittenAwait;
            }

            // The await expression will be lowered to code that involves the use of side-effects
            // such as jumps and labels, which we can only emit with an empty stack, so we require
            // that the await expression itself is produced only when the stack is empty.
            // Therefore it is represented by a BoundSpillSequence.  The resulting nodes will be "spilled" to move
            // such statements to the top level (i.e. into the enclosing statement list).  Here we ensure
            // that the await result itself is stored into a temp at the statement level, as that is
            // the form handled by async lowering.
            _needsSpilling = true;
            var nonConditional = rewrittenAwait.Update(false, rewrittenAwait.Expression, rewrittenAwait.AwaitableInfo, rewrittenAwait.AwaitableInfo.GetResult!.ReturnType);
            BoundSpillSequence awaitedResult;

            if (rewrittenAwait.Type.IsVoidType())
            {
                Debug.Assert(!used);
                Debug.Assert(rewrittenAwait.IsConditional);
                awaitedResult = new BoundSpillSequence(
                    syntax: rewrittenAwait.Syntax,
                    locals: ImmutableArray<LocalSymbol>.Empty,
                    sideEffects: ImmutableArray<BoundExpression>.Empty,
                    value: nonConditional,
                    type: nonConditional.Type);
            }
            else
            {
                var tempAccess = _factory.StoreToTemp(nonConditional, out BoundAssignmentOperator tempAssignment, syntaxOpt: rewrittenAwait.Syntax,
                    kind: SynthesizedLocalKind.Spill);
                awaitedResult = new BoundSpillSequence(
                    syntax: rewrittenAwait.Syntax,
                    locals: ImmutableArray.Create<LocalSymbol>(tempAccess.LocalSymbol),
                    sideEffects: ImmutableArray.Create<BoundExpression>(tempAssignment),
                    value: tempAccess,
                    type: tempAccess.Type);
            }

            if (!rewrittenAwait.IsConditional)
            {
                return awaitedResult;
            }

            // Conditional await

            var loweredExpression = rewrittenAwait.Expression;
            Debug.Assert(loweredExpression.Type is { });
            var receiverType = loweredExpression.Type;

            // Check trivial case
            if (loweredExpression.IsDefaultValue() && receiverType.IsReferenceType)
            {
                return _factory.Default(rewrittenAwait.Type);
            }

            BoundLocal? temp = null;
            BoundAssignmentOperator? store = null;
            if (CanChangeValueBetweenReads(loweredExpression))
            {
                temp = _factory.StoreToTemp(loweredExpression, out store);
                loweredExpression = temp;
            }

            var condition = MakeNullCheck(rewrittenAwait.Syntax, loweredExpression, BinaryOperatorKind.NotEqual);

            BoundExpression consequence;
            if (!awaitedResult.Type.IsVoidType() && awaitedResult.Type.IsNonNullableValueType())
            {
                consequence = RewriteNullableConversion(rewrittenAwait.Syntax,
                    awaitedResult,
                    Conversion.MakeNullableConversion(ConversionKind.ImplicitNullable, Conversion.Identity),
                    false,
                    false,
                    rewrittenAwait.Type);
            }
            else
            {
                consequence = awaitedResult;
            }

            var result = RewriteConditionalOperator(rewrittenAwait.Syntax,
                condition,
                consequence,
                _factory.Default(rewrittenAwait.Type),
                null,
                rewrittenAwait.Type,
                false);

            if (temp is not null)
            {
                Debug.Assert(store is not null);
                result = _factory.MakeSequence(temp.LocalSymbol,
                    store,
                    result);
            }

            return result;
        }
    }
}
