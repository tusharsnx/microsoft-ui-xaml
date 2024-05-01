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

#include "precomp.h"
#include "DragStartedEventArgs.g.h"
#include "CoreEventArgsGroup.h"

using namespace DirectUI;

// Constructors/destructors.
DirectUI::DragStartedEventArgsGenerated::DragStartedEventArgsGenerated()
{
}

DirectUI::DragStartedEventArgsGenerated::~DragStartedEventArgsGenerated()
{
}

HRESULT DirectUI::DragStartedEventArgsGenerated::QueryInterfaceImpl(_In_ REFIID iid, _Outptr_ void** ppObject)
{
    if (InlineIsEqualGUID(iid, __uuidof(DirectUI::DragStartedEventArgs)))
    {
        *ppObject = static_cast<DirectUI::DragStartedEventArgs*>(this);
    }
    else if (InlineIsEqualGUID(iid, __uuidof(ABI::Microsoft::UI::Xaml::Controls::Primitives::IDragStartedEventArgs)))
    {
        *ppObject = static_cast<ABI::Microsoft::UI::Xaml::Controls::Primitives::IDragStartedEventArgs*>(this);
    }
    else
    {
        RRETURN(DirectUI::RoutedEventArgs::QueryInterfaceImpl(iid, ppObject));
    }

    AddRefOuter();
    RRETURN(S_OK);
}


// Properties.
IFACEMETHODIMP DirectUI::DragStartedEventArgsGenerated::get_HorizontalOffset(_Out_ DOUBLE* pValue)
{
    HRESULT hr = S_OK;
    ARG_VALIDRETURNPOINTER(pValue);
    IFC(CheckThread());
    IFC(static_cast<DragStartedEventArgs*>(this)->get_HorizontalOffsetImpl(pValue));
Cleanup:
    RRETURN(hr);
}
_Check_return_ HRESULT DirectUI::DragStartedEventArgsGenerated::put_HorizontalOffset(_In_ DOUBLE value)
{
    HRESULT hr = S_OK;
    
    IFC(CheckThread());
    IFC(static_cast<DragStartedEventArgs*>(this)->put_HorizontalOffsetImpl(value));
Cleanup:
    RRETURN(hr);
}
IFACEMETHODIMP DirectUI::DragStartedEventArgsGenerated::get_VerticalOffset(_Out_ DOUBLE* pValue)
{
    HRESULT hr = S_OK;
    ARG_VALIDRETURNPOINTER(pValue);
    IFC(CheckThread());
    IFC(static_cast<DragStartedEventArgs*>(this)->get_VerticalOffsetImpl(pValue));
Cleanup:
    RRETURN(hr);
}
_Check_return_ HRESULT DirectUI::DragStartedEventArgsGenerated::put_VerticalOffset(_In_ DOUBLE value)
{
    HRESULT hr = S_OK;
    
    IFC(CheckThread());
    IFC(static_cast<DragStartedEventArgs*>(this)->put_VerticalOffsetImpl(value));
Cleanup:
    RRETURN(hr);
}

// Methods.

HRESULT DirectUI::DragStartedEventArgsFactory::QueryInterfaceImpl(_In_ REFIID iid, _Outptr_ void** ppObject)
{
    if (InlineIsEqualGUID(iid, __uuidof(ABI::Microsoft::UI::Xaml::Controls::Primitives::IDragStartedEventArgsFactory)))
    {
        *ppObject = static_cast<ABI::Microsoft::UI::Xaml::Controls::Primitives::IDragStartedEventArgsFactory*>(this);
    }
    else
    {
        RRETURN(ctl::AggregableActivationFactory<DirectUI::DragStartedEventArgs>::QueryInterfaceImpl(iid, ppObject));
    }

    AddRefOuter();
    RRETURN(S_OK);
}


// Factory methods.
IFACEMETHODIMP DirectUI::DragStartedEventArgsFactory::CreateInstanceWithHorizontalOffsetAndVerticalOffset(_In_ DOUBLE horizontalOffset, _In_ DOUBLE verticalOffset, _In_opt_ IInspectable* pOuter, _Outptr_ IInspectable** ppInner, _Outptr_ ABI::Microsoft::UI::Xaml::Controls::Primitives::IDragStartedEventArgs** ppInstance)
{
    HRESULT hr = S_OK;
    
    ARG_VALIDRETURNPOINTER(ppInstance);
    IFC(CreateInstanceWithHorizontalOffsetAndVerticalOffsetImpl(horizontalOffset, verticalOffset, pOuter, ppInner, ppInstance));
Cleanup:
    return hr;
}

// Dependency properties.

// Attached properties.

// Static properties.

// Static methods.

namespace DirectUI
{
    _Check_return_ HRESULT OnFrameworkCreateDragStartedEventArgs(_In_ CEventArgs* pCoreObject, _Out_ IInspectable** ppNewInstance)
    {
        HRESULT hr = S_OK;
        ctl::ComPtr<DirectUI::DragStartedEventArgs> spInstance;
        *ppNewInstance = nullptr;
        IFC(ctl::make(pCoreObject, &spInstance));
        *ppNewInstance = ctl::iinspectable_cast(spInstance.Detach());
    Cleanup:
        RRETURN(hr);
    }
    _Check_return_ IActivationFactory* CreateActivationFactory_DragStartedEventArgs()
    {
        RRETURN(ctl::ActivationFactoryCreator<DragStartedEventArgsFactory>::CreateActivationFactory());
    }
}