// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
//------------------------------------------------------------------------
//
//  Abstract:
//
//      XAML types.
//      NOTE: This file was generated by a tool.
//
//------------------------------------------------------------------------

#pragma once


#define __TextBoxTextChangingEventArgs_GUID "239f56d3-473f-4ff8-a69b-ae869e20dbe3"

namespace DirectUI
{
    class TextBoxTextChangingEventArgs;

    class __declspec(novtable) __declspec(uuid(__TextBoxTextChangingEventArgs_GUID)) TextBoxTextChangingEventArgs :
        public ABI::Microsoft::UI::Xaml::Controls::ITextBoxTextChangingEventArgs,
        public DirectUI::EventArgs
    {

        INSPECTABLE_CLASS(L"Microsoft.UI.Xaml.Controls.TextBoxTextChangingEventArgs");

        BEGIN_INTERFACE_MAP(TextBoxTextChangingEventArgs, DirectUI::EventArgs)
            INTERFACE_ENTRY(TextBoxTextChangingEventArgs, ABI::Microsoft::UI::Xaml::Controls::ITextBoxTextChangingEventArgs)
        END_INTERFACE_MAP(TextBoxTextChangingEventArgs, DirectUI::EventArgs)

    public:
        TextBoxTextChangingEventArgs();
        ~TextBoxTextChangingEventArgs() override;

        // Properties.
        IFACEMETHOD(get_IsContentChanging)(_Out_ BOOLEAN* pValue) override;
        _Check_return_ HRESULT put_IsContentChanging(_In_ BOOLEAN value);

        // Methods.

    protected:
        HRESULT QueryInterfaceImpl(_In_ REFIID iid, _Outptr_ void** ppObject) override;

    private:
        // Customized properties.

        // Customized methods.

        // Fields.
        BOOLEAN m_isContentChanging;
    };
}

