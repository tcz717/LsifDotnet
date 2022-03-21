using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;

namespace LsifDotnet.Roslyn;

public class CSharpIdentifierCollector : CSharpSyntaxWalker
{
    public ILogger<CSharpIdentifierCollector> Logger { get; }

    public CSharpIdentifierCollector(ILogger<CSharpIdentifierCollector> logger)
    {
        Logger = logger;
    }

    public List<SyntaxToken> IdentifierList { get; set; } = new();

    public override void VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        Logger.LogTrace($"Class {node.Identifier}");
        IdentifierList.Add(node.Identifier);

        base.VisitClassDeclaration(node);
    }

    public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        Logger.LogTrace($"Method {node.Identifier}");
        IdentifierList.Add(node.Identifier);

        base.VisitMethodDeclaration(node);
    }

    public override void VisitVariableDeclarator(VariableDeclaratorSyntax node)
    {
        Logger.LogTrace($"Variable {node.Identifier}");
        IdentifierList.Add(node.Identifier);

        base.VisitVariableDeclarator(node);
    }

    public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
    {
        Logger.LogTrace($"Ctor {node.Identifier}");
        IdentifierList.Add(node.Identifier);

        base.VisitConstructorDeclaration(node);
    }

    public override void VisitSingleVariableDesignation(SingleVariableDesignationSyntax node)
    {
        Logger.LogTrace($"Single Var Designation {node.Identifier}");
        IdentifierList.Add(node.Identifier);

        base.VisitSingleVariableDesignation(node);
    }

    public override void VisitParameter(ParameterSyntax node)
    {
        Logger.LogTrace($"Parameter {node.Identifier}");
        IdentifierList.Add(node.Identifier);
        base.VisitParameter(node);
    }

    public override void VisitStructDeclaration(StructDeclarationSyntax node)
    {
        Logger.LogTrace($"Struct {node.Identifier}");
        IdentifierList.Add(node.Identifier);
        base.VisitStructDeclaration(node);
    }

    public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
    {
        Logger.LogTrace($"Interface {node.Identifier}");
        IdentifierList.Add(node.Identifier);
        base.VisitInterfaceDeclaration(node);
    }

    public override void VisitRecordDeclaration(RecordDeclarationSyntax node)
    {
        Logger.LogTrace($"Record {node.Identifier}");
        IdentifierList.Add(node.Identifier);
        base.VisitRecordDeclaration(node);
    }

    public override void VisitEnumDeclaration(EnumDeclarationSyntax node)
    {
        Logger.LogTrace($"Enum {node.Identifier}");
        IdentifierList.Add(node.Identifier);
        base.VisitEnumDeclaration(node);
    }

    public override void VisitDelegateDeclaration(DelegateDeclarationSyntax node)
    {
        Logger.LogTrace($"Delegate {node.Identifier}");
        IdentifierList.Add(node.Identifier);
        base.VisitDelegateDeclaration(node);
    }

    public override void VisitEnumMemberDeclaration(EnumMemberDeclarationSyntax node)
    {
        Logger.LogTrace($"EnumMember {node.Identifier}");
        IdentifierList.Add(node.Identifier);
        base.VisitEnumMemberDeclaration(node);
    }


    public override void VisitDestructorDeclaration(DestructorDeclarationSyntax node)
    {
        Logger.LogTrace($"Destructor {node.Identifier}");
        IdentifierList.Add(node.Identifier);
        base.VisitDestructorDeclaration(node);
    }

    public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
    {
        Logger.LogTrace($"Property {node.Identifier}");
        IdentifierList.Add(node.Identifier);
        base.VisitPropertyDeclaration(node);
    }

    public override void VisitEventDeclaration(EventDeclarationSyntax node)
    {
        Logger.LogTrace($"Event {node.Identifier}");
        IdentifierList.Add(node.Identifier);
        base.VisitEventDeclaration(node);
    }

    public override void VisitGenericName(GenericNameSyntax node)
    {
        Logger.LogTrace($"Generic {node.Identifier}");
        IdentifierList.Add(node.Identifier);
        base.VisitGenericName(node);
    }

    public override void VisitIdentifierName(IdentifierNameSyntax node)
    {
        Logger.LogTrace($"Ident {node.Identifier}");
        IdentifierList.Add(node.Identifier);
        base.VisitIdentifierName(node);
    }
}