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

#include "FrameworkElementAutomationPeer.g.h"

#define __CalendarDatePickerAutomationPeer_GUID "2894679d-e8bd-485c-9ecf-63eef126aee6"

namespace DirectUI
{
    class CalendarDatePickerAutomationPeer;

    class __declspec(novtable) CalendarDatePickerAutomationPeerGenerated:
        public DirectUI::FrameworkElementAutomationPeer
        , public ABI::Microsoft::UI::Xaml::Automation::Peers::ICalendarDatePickerAutomationPeer
        , public ABI::Microsoft::UI::Xaml::Automation::Provider::IInvokeProvider
        , public ABI::Microsoft::UI::Xaml::Automation::Provider::IValueProvider
    {
        friend class DirectUI::CalendarDatePickerAutomationPeer;

        INSPECTABLE_CLASS(L"Microsoft.UI.Xaml.Automation.Peers.CalendarDatePickerAutomationPeer");

        BEGIN_INTERFACE_MAP(CalendarDatePickerAutomationPeerGenerated, DirectUI::FrameworkElementAutomationPeer)
            INTERFACE_ENTRY(CalendarDatePickerAutomationPeerGenerated, ABI::Microsoft::UI::Xaml::Automation::Peers::ICalendarDatePickerAutomationPeer)
            INTERFACE_ENTRY(CalendarDatePickerAutomationPeerGenerated, ABI::Microsoft::UI::Xaml::Automation::Provider::IInvokeProvider)
            INTERFACE_ENTRY(CalendarDatePickerAutomationPeerGenerated, ABI::Microsoft::UI::Xaml::Automation::Provider::IValueProvider)
        END_INTERFACE_MAP(CalendarDatePickerAutomationPeerGenerated, DirectUI::FrameworkElementAutomationPeer)

    public:
        CalendarDatePickerAutomationPeerGenerated();
        ~CalendarDatePickerAutomationPeerGenerated() override;

        // Event source typedefs.

        KnownTypeIndex GetTypeIndex() const override
        {
            return KnownTypeIndex::CalendarDatePickerAutomationPeer;
        }

        static XCP_FORCEINLINE KnownTypeIndex GetTypeIndexStatic()
        {
            return KnownTypeIndex::CalendarDatePickerAutomationPeer;
        }

        // Properties.
        IFACEMETHOD(get_IsReadOnly)(_Out_ BOOLEAN* pValue) override;
        IFACEMETHOD(get_Value)(_Out_ HSTRING* pValue) override;

        // Events.

        // Methods.
        IFACEMETHOD(Invoke)() override;
        IFACEMETHOD(SetValue)(_In_ HSTRING value) override;


    protected:
        HRESULT QueryInterfaceImpl(_In_ REFIID iid, _Outptr_ void** ppObject) override;

    private:

        // Fields.
    };
}

#include "CalendarDatePickerAutomationPeer_Partial.h"

namespace DirectUI
{
    // Note that the ordering of the base types here is important - the base factory comes first, followed by all the
    // interfaces specific to this type.  By doing this, we allow every Factory's CreateInstance method to be more
    // COMDAT-folding-friendly.  Because this ensures that the first vfptr contains GetTypeIndex, it means that all
    // CreateInstance functions with the same base factory generate the same assembly instructions and thus will
    // fold together.  This is significant for binary size in Microsoft.UI.Xaml.dll so change this only with great
    // care.
    class __declspec(novtable) CalendarDatePickerAutomationPeerFactory:
       public ctl::BetterAggregableCoreObjectActivationFactory
        , public ABI::Microsoft::UI::Xaml::Automation::Peers::ICalendarDatePickerAutomationPeerFactory
    {
        BEGIN_INTERFACE_MAP(CalendarDatePickerAutomationPeerFactory, ctl::BetterAggregableCoreObjectActivationFactory)
            INTERFACE_ENTRY(CalendarDatePickerAutomationPeerFactory, ABI::Microsoft::UI::Xaml::Automation::Peers::ICalendarDatePickerAutomationPeerFactory)
        END_INTERFACE_MAP(CalendarDatePickerAutomationPeerFactory, ctl::BetterAggregableCoreObjectActivationFactory)

    public:
        // Factory methods.
        IFACEMETHOD(CreateInstanceWithOwner)(_In_ ABI::Microsoft::UI::Xaml::Controls::ICalendarDatePicker* pOwner, _In_opt_ IInspectable* pOuter, _Outptr_ IInspectable** ppInner, _Outptr_ ABI::Microsoft::UI::Xaml::Automation::Peers::ICalendarDatePickerAutomationPeer** ppInstance);

        // Static properties.

        // Dependency properties.

        // Attached properties.

        // Static methods.

        // Static events.

    protected:
        HRESULT QueryInterfaceImpl(_In_ REFIID iid, _Outptr_ void** ppObject) override;

        KnownTypeIndex GetTypeIndex() const override
        {
            return KnownTypeIndex::CalendarDatePickerAutomationPeer;
        }


    private:
        _Check_return_ HRESULT CreateInstanceWithOwnerImpl(_In_ ABI::Microsoft::UI::Xaml::Controls::ICalendarDatePicker* pOwner, _In_opt_ IInspectable* pOuter, _Outptr_ IInspectable** ppInner, _Outptr_ ABI::Microsoft::UI::Xaml::Automation::Peers::ICalendarDatePickerAutomationPeer** ppInstance);

        // Customized static properties.

        // Customized static  methods.
    };
}