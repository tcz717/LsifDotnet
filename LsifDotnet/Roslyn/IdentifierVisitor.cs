using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LsifDotnet.Roslyn;

public class IdentifierVisitor : CSharpSyntaxWalker
{
    public List<SyntaxToken> IdentifierList { get; set; } = new List<SyntaxToken>();

    public override void VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        Console.WriteLine($"Class {node.Identifier}");
        IdentifierList.Add(node.Identifier);
    
        base.VisitClassDeclaration(node);
    }
    
    public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        Console.WriteLine($"Method {node.Identifier}");
        IdentifierList.Add(node.Identifier);
    
        base.VisitMethodDeclaration(node);
    }
    
    public override void VisitVariableDeclarator(VariableDeclaratorSyntax node)
    {
        Console.WriteLine($"Variable {node.Identifier}");
        IdentifierList.Add(node.Identifier);
    
        base.VisitVariableDeclarator(node);
    }

    public override void VisitIdentifierName(IdentifierNameSyntax node)
    {
        Console.WriteLine($"Ident {node.Identifier}");
        IdentifierList.Add(node.Identifier);
        base.VisitIdentifierName(node);
    }
}