﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable disable

using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp.Test.Utilities;
using Microsoft.CodeAnalysis.CSharp.UnitTests;
using Microsoft.CodeAnalysis.Test.Extensions;
using Microsoft.CodeAnalysis.Test.Utilities;
using Roslyn.Test.Utilities;
using Xunit;

namespace Microsoft.CodeAnalysis.CSharp.Semantic.UnitTests.Semantics
{
    public class AwaitConditionalTests : CompilingTestBase
    {
        private static readonly CSharpParseOptions WithConditionalAwait = TestOptions.Regular.WithLanguageVersion(MessageID.IDS_FeatureConditionalAwait.RequiredVersion());

        [Fact]
        public void TestConditionalAwaitTask()
        {
            const string source = @"
using System.Threading.Tasks;

class C
{
    public async void M(Task t)
    {
        await? t;
    }
}
";
            var info = GetAwaitExpressionInfo(source, out var compilation);
            Assert.Equal("System.Runtime.CompilerServices.TaskAwaiter System.Threading.Tasks.Task.GetAwaiter()", info.GetAwaiterMethod.ToTestDisplayString());
            Assert.Equal("void System.Runtime.CompilerServices.TaskAwaiter.GetResult()", info.GetResultMethod.ToTestDisplayString());
            Assert.Equal("System.Boolean System.Runtime.CompilerServices.TaskAwaiter.IsCompleted { get; }", info.IsCompletedProperty.ToTestDisplayString());
        }

        [Fact]
        public void TestConditionalAwaitValueTask()
        {
            const string source = @"
using System.Threading.Tasks;

class C
{
    public async void M(ValueTask? t)
    {
        await? t;
    }
}
";
            var info = GetAwaitExpressionInfo(source, out var compilation);
            Assert.Equal("System.Runtime.CompilerServices.ValueTaskAwaiter System.Threading.Tasks.ValueTask.GetAwaiter()", info.GetAwaiterMethod.ToTestDisplayString());
            Assert.Equal("void System.Runtime.CompilerServices.ValueTaskAwaiter.GetResult()", info.GetResultMethod.ToTestDisplayString());
            Assert.Equal("System.Boolean System.Runtime.CompilerServices.ValueTaskAwaiter.IsCompleted { get; }", info.IsCompletedProperty.ToTestDisplayString());
        }

        [Fact]
        public void TestConditionalAwaitNonNullable()
        {
            const string source = @"
using System.Threading.Tasks;

class C
{
    public async void M(ValueTask t)
    {
        await? t;
    }
}
";
            var info = GetAwaitExpressionInfo(source, out var compilation,
                Diagnostic(ErrorCode.ERR_BadUnaryOp, "await? t")
                    .WithArguments("await?", "System.Threading.Tasks.ValueTask")
                    .WithLocation(8, 9));
        }

        private AwaitExpressionInfo GetAwaitExpressionInfo(string text, out CSharpCompilation compilation, params DiagnosticDescription[] diagnostics)
        {
            var tree = Parse(text, options: WithConditionalAwait);
            var comp = CreateCompilation(new SyntaxTree[] { tree }, targetFramework: TargetFramework.NetCoreApp30);
            comp.VerifyDiagnostics(diagnostics);
            compilation = comp;
            var syntaxNode = (AwaitExpressionSyntax)tree.FindNodeOrTokenByKind(SyntaxKind.AwaitExpression).AsNode();
            var treeModel = comp.GetSemanticModel(tree);
            return treeModel.GetAwaitExpressionInfo(syntaxNode);
        }
    }
}
