// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// ------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     Runtime Version: 14.0.0.0
//  
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
// ------------------------------------------------------------------------------
namespace XamlGen.Templates.Metadata
{
    using System.Linq;
    using System.Text;
    using System.Collections.Generic;
    using OM;
    using System;
    
    /// <summary>
    /// Class to produce the template output
    /// </summary>
    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("Microsoft.VisualStudio.TextTemplating", "14.0.0.0")]
    public partial class TypeTableSlots : MetadataCodeGenerator<OMContextView>
    {
        /// <summary>
        /// Create the template output
        /// </summary>
        public override string TransformText()
        {
            this.Write(this.ToStringHelper.ToStringWithCulture(IncludeTemplate<Copyright>()));
            this.Write(@"

#include ""precomp.h""
#include ""Indexes.g.h""

// Returns the number of slots (number of properties, including inherited properties from ancestors) a type has.
XUINT8 GetTypeSlotCount(_In_ KnownTypeIndex nTypeIndex)
{
    switch (nTypeIndex)
    {
");
 foreach (var type in Model.GetAllTypeTableClassesWithSlottedProperties()) { 
            this.Write("        case ");
            this.Write(this.ToStringHelper.ToStringWithCulture(type.IndexName));
            this.Write(": return ");
            this.Write(this.ToStringHelper.ToStringWithCulture(type.GetSlottedProperties().Count()));
            this.Write(";\r\n");
 } 
            this.Write(@"    }

    // We should never reach this.
    ASSERT(FALSE);
    return 0;
}

// Returns the slot number of a property for a given type.
XUINT8 GetPropertySlot(_In_ KnownTypeIndex nTypeIndex, _In_ KnownPropertyIndex nPropertyIndex)
{
    switch (nTypeIndex)
    {
");
 foreach (var type in Model.GetAllTypeTableClassesWithSlottedProperties()) { 
            this.Write("        case ");
            this.Write(this.ToStringHelper.ToStringWithCulture(type.IndexName));
            this.Write(":\r\n            switch (nPropertyIndex)\r\n            {\r\n");
     foreach (var property in type.GetSlottedProperties()) { 
            this.Write("                case ");
            this.Write(this.ToStringHelper.ToStringWithCulture(property.IndexName));
            this.Write(": return ");
            this.Write(this.ToStringHelper.ToStringWithCulture(property.GetSlotFor(type)));
            this.Write(";\r\n");
     } 
            this.Write("            }\r\n");
 } 
            this.Write("    }\r\n\r\n    // We should never reach this.\r\n    ASSERT(FALSE);\r\n    return 0;\r\n}" +
                    "\r\n");
            return this.GenerationEnvironment.ToString();
        }
    }
}