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

    public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
    {
        Console.WriteLine($"Ctor {node.Identifier}");
        IdentifierList.Add(node.Identifier);

        base.VisitConstructorDeclaration(node);
    }

    public override void VisitSingleVariableDesignation(SingleVariableDesignationSyntax node)
    {
        Console.WriteLine($"Single Var Designation {node.Identifier}");
        IdentifierList.Add(node.Identifier);

        base.VisitSingleVariableDesignation(node);
    }

    public override void VisitParameter(ParameterSyntax node)
    {
        Console.WriteLine($"Parameter {node.Identifier}");
        IdentifierList.Add(node.Identifier);
        base.VisitParameter(node);
    }

    public override void VisitStructDeclaration(StructDeclarationSyntax node)
    {
        Console.WriteLine($"Struct {node.Identifier}");
        IdentifierList.Add(node.Identifier);
        base.VisitStructDeclaration(node);
    }

    public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
    {
        Console.WriteLine($"Interface {node.Identifier}");
        IdentifierList.Add(node.Identifier);
        base.VisitInterfaceDeclaration(node);
    }

    public override void VisitRecordDeclaration(RecordDeclarationSyntax node)
    {
        Console.WriteLine($"Record {node.Identifier}");
        IdentifierList.Add(node.Identifier);
        base.VisitRecordDeclaration(node);
    }

    public override void VisitEnumDeclaration(EnumDeclarationSyntax node)
    {
        Console.WriteLine($"Enum {node.Identifier}");
        IdentifierList.Add(node.Identifier);
        base.VisitEnumDeclaration(node);
    }

    public override void VisitDelegateDeclaration(DelegateDeclarationSyntax node)
    {
        Console.WriteLine($"Delegate {node.Identifier}");
        IdentifierList.Add(node.Identifier);
        base.VisitDelegateDeclaration(node);
    }

    public override void VisitEnumMemberDeclaration(EnumMemberDeclarationSyntax node)
    {
        Console.WriteLine($"EnumMember {node.Identifier}");
        IdentifierList.Add(node.Identifier);
        base.VisitEnumMemberDeclaration(node);
    }
    

    public override void VisitDestructorDeclaration(DestructorDeclarationSyntax node)
    {
        Console.WriteLine($"Destructor {node.Identifier}");
        IdentifierList.Add(node.Identifier);
        base.VisitDestructorDeclaration(node);
    }

    public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
    {
        Console.WriteLine($"Property {node.Identifier}");
        IdentifierList.Add(node.Identifier);
        base.VisitPropertyDeclaration(node);
    }

    public override void VisitEventDeclaration(EventDeclarationSyntax node)
    {
        Console.WriteLine($"Event {node.Identifier}");
        IdentifierList.Add(node.Identifier);
        base.VisitEventDeclaration(node);
    }

    public override void VisitGenericName(GenericNameSyntax node)
    {
        Console.WriteLine($"Generic {node.Identifier}");
        IdentifierList.Add(node.Identifier);
        base.VisitGenericName(node);
    }

    public override void VisitIdentifierName(IdentifierNameSyntax node)
    {
        Console.WriteLine($"Ident {node.Identifier}");
        IdentifierList.Add(node.Identifier);
        base.VisitIdentifierName(node);
    }
}