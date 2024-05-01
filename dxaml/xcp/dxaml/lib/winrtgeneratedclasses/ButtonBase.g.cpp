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

#include "ButtonBase.g.h"
#include "XamlTelemetry.h"

// Constructors/destructors.
DirectUI::ButtonBaseGenerated::ButtonBaseGenerated()
{
}

DirectUI::ButtonBaseGenerated::~ButtonBaseGenerated()
{
}

HRESULT DirectUI::ButtonBaseGenerated::QueryInterfaceImpl(_In_ REFIID iid, _Outptr_ void** ppObject)
{
    if (InlineIsEqualGUID(iid, __uuidof(DirectUI::ButtonBase)))
    {
        *ppObject = static_cast<DirectUI::ButtonBase*>(this);
    }
    else if (InlineIsEqualGUID(iid, __uuidof(ABI::Microsoft::UI::Xaml::Controls::Primitives::IButtonBase)))
    {
        *ppObject = static_cast<ABI::Microsoft::UI::Xaml::Controls::Primitives::IButtonBase*>(this);
    }
    else
    {
        RRETURN(DirectUI::ContentControl::QueryInterfaceImpl(iid, ppObject));
    }

    AddRefOuter();
    RRETURN(S_OK);
}

// Properties.
IFACEMETHODIMP DirectUI::ButtonBaseGenerated::get_ClickMode(_Out_ ABI::Microsoft::UI::Xaml::Controls::ClickMode* pValue)
{
    RRETURN(GetValueByKnownIndex(KnownPropertyIndex::ButtonBase_ClickMode, pValue));
}
IFACEMETHODIMP DirectUI::ButtonBaseGenerated::put_ClickMode(_In_ ABI::Microsoft::UI::Xaml::Controls::ClickMode value)
{
    IFC_RETURN(DefaultStrictApiCheck(this));
    RRETURN(SetValueByKnownIndex(KnownPropertyIndex::ButtonBase_ClickMode, value));
}
IFACEMETHODIMP DirectUI::ButtonBaseGenerated::get_Command(_Outptr_result_maybenull_ ABI::Microsoft::UI::Xaml::Input::ICommand** ppValue)
{
    RRETURN(GetValueByKnownIndex(KnownPropertyIndex::ButtonBase_Command, ppValue));
}
IFACEMETHODIMP DirectUI::ButtonBaseGenerated::put_Command(_In_opt_ ABI::Microsoft::UI::Xaml::Input::ICommand* pValue)
{
    IFC_RETURN(DefaultStrictApiCheck(this));
    RRETURN(SetValueByKnownIndex(KnownPropertyIndex::ButtonBase_Command, pValue));
}
IFACEMETHODIMP DirectUI::ButtonBaseGenerated::get_CommandParameter(_Outptr_result_maybenull_ IInspectable** ppValue)
{
    RRETURN(GetValueByKnownIndex(KnownPropertyIndex::ButtonBase_CommandParameter, ppValue));
}
IFACEMETHODIMP DirectUI::ButtonBaseGenerated::put_CommandParameter(_In_opt_ IInspectable* pValue)
{
    IFC_RETURN(DefaultStrictApiCheck(this));
    RRETURN(SetValueByKnownIndex(KnownPropertyIndex::ButtonBase_CommandParameter, pValue));
}
IFACEMETHODIMP DirectUI::ButtonBaseGenerated::get_IsPointerOver(_Out_ BOOLEAN* pValue)
{
    RRETURN(GetValueByKnownIndex(KnownPropertyIndex::ButtonBase_IsPointerOver, pValue));
}
_Check_return_ HRESULT DirectUI::ButtonBaseGenerated::put_IsPointerOver(_In_ BOOLEAN value)
{
    IFC_RETURN(DefaultStrictApiCheck(this));
    RRETURN(SetValueByKnownIndex(KnownPropertyIndex::ButtonBase_IsPointerOver, value));
}
IFACEMETHODIMP DirectUI::ButtonBaseGenerated::get_IsPressed(_Out_ BOOLEAN* pValue)
{
    RRETURN(GetValueByKnownIndex(KnownPropertyIndex::ButtonBase_IsPressed, pValue));
}
_Check_return_ HRESULT DirectUI::ButtonBaseGenerated::put_IsPressed(_In_ BOOLEAN value)
{
    IFC_RETURN(DefaultStrictApiCheck(this));
    RRETURN(SetValueByKnownIndex(KnownPropertyIndex::ButtonBase_IsPressed, value));
}

// Events.
_Check_return_ HRESULT DirectUI::ButtonBaseGenerated::GetClickEventSourceNoRef(_Outptr_ ClickEventSourceType** ppEventSource)
{
    HRESULT hr = S_OK;

    IFC(GetEventSourceNoRefWithArgumentValidation(KnownEventIndex::ButtonBase_Click, reinterpret_cast<IUntypedEventSource**>(ppEventSource)));

    if (*ppEventSource == nullptr)
    {
        IFC(ctl::ComObject<ClickEventSourceType>::CreateInstance(ppEventSource, TRUE /* fDisableLeakChecks */));
        (*ppEventSource)->Initialize(KnownEventIndex::ButtonBase_Click, this, /* bUseEventManager */ false);
        IFC(StoreEventSource(KnownEventIndex::ButtonBase_Click, *ppEventSource));

        // The caller doesn't expect a ref, this function ends in "NoRef".  The ref is now owned by the map (inside StoreEventSource)
        ReleaseInterfaceNoNULL(ctl::iunknown_cast(*ppEventSource));
    }

Cleanup:
    return hr;
}

IFACEMETHODIMP DirectUI::ButtonBaseGenerated::add_Click(_In_ ABI::Microsoft::UI::Xaml::IRoutedEventHandler* pValue, _Out_ EventRegistrationToken* ptToken)
{
    HRESULT hr = S_OK;
    ClickEventSourceType* pEventSource = nullptr;

    IFC(EventAddPreValidation(pValue, ptToken));
    IFC(DefaultStrictApiCheck(this));
    IFC(GetClickEventSourceNoRef(&pEventSource));
    IFC(pEventSource->AddHandler(pValue));

    ptToken->value = (INT64)pValue;

Cleanup:
    return hr;
}

IFACEMETHODIMP DirectUI::ButtonBaseGenerated::remove_Click(_In_ EventRegistrationToken tToken)
{
    HRESULT hr = S_OK;
    ClickEventSourceType* pEventSource = nullptr;
    ABI::Microsoft::UI::Xaml::IRoutedEventHandler* pValue = (ABI::Microsoft::UI::Xaml::IRoutedEventHandler*)tToken.value;

    IFC(CheckThread());
    IFC(DefaultStrictApiCheck(this));
    IFC(GetClickEventSourceNoRef(&pEventSource));
    IFC(pEventSource->RemoveHandler(pValue));

    if (!pEventSource->HasHandlers())
    {
        IFC(RemoveEventSource(KnownEventIndex::ButtonBase_Click));
    }

Cleanup:
    RRETURN(hr);
}

// Methods.

_Check_return_ HRESULT DirectUI::ButtonBaseGenerated::EventAddHandlerByIndex(_In_ KnownEventIndex nEventIndex, _In_ IInspectable* pHandler, _In_ BOOLEAN handledEventsToo)
{
    switch (nEventIndex)
    {
    case KnownEventIndex::ButtonBase_Click:
        {
            ctl::ComPtr<ABI::Microsoft::UI::Xaml::IRoutedEventHandler> spEventHandler;
            IValueBoxer::UnboxValue(pHandler, spEventHandler.ReleaseAndGetAddressOf());

            if (nullptr != spEventHandler)
            {
                ClickEventSourceType* pEventSource = nullptr;
                IFC_RETURN(GetClickEventSourceNoRef(&pEventSource));
                IFC_RETURN(pEventSource->AddHandler(spEventHandler.Get(), handledEventsToo));
            }
            else
            {
                IFC_RETURN(E_INVALIDARG);
            }
        }
        break;
    default:
        IFC_RETURN(DirectUI::ContentControlGenerated::EventAddHandlerByIndex(nEventIndex, pHandler, handledEventsToo));
        break;
    }

    return S_OK;
}

_Check_return_ HRESULT DirectUI::ButtonBaseGenerated::EventRemoveHandlerByIndex(_In_ KnownEventIndex nEventIndex, _In_ IInspectable* pHandler)
{
    switch (nEventIndex)
    {
    case KnownEventIndex::ButtonBase_Click:
        {
            ctl::ComPtr<ABI::Microsoft::UI::Xaml::IRoutedEventHandler> spEventHandler;
            IValueBoxer::UnboxValue(pHandler, spEventHandler.ReleaseAndGetAddressOf());

            if (nullptr != spEventHandler)
            {
                ClickEventSourceType* pEventSource = nullptr;
                IFC_RETURN(GetClickEventSourceNoRef(&pEventSource));
                IFC_RETURN(pEventSource->RemoveHandler(spEventHandler.Get()));
            }
            else
            {
                IFC_RETURN(E_INVALIDARG);
            }
        }
        break;
    default:
        IFC_RETURN(DirectUI::ContentControlGenerated::EventRemoveHandlerByIndex(nEventIndex, pHandler));
        break;
    }

    return S_OK;
}

HRESULT DirectUI::ButtonBaseFactory::QueryInterfaceImpl(_In_ REFIID iid, _Outptr_ void** ppObject)
{
    if (InlineIsEqualGUID(iid, __uuidof(ABI::Microsoft::UI::Xaml::Controls::Primitives::IButtonBaseFactory)))
    {
        *ppObject = static_cast<ABI::Microsoft::UI::Xaml::Controls::Primitives::IButtonBaseFactory*>(this);
    }
    else if (InlineIsEqualGUID(iid, __uuidof(ABI::Microsoft::UI::Xaml::Controls::Primitives::IButtonBaseStatics)))
    {
        *ppObject = static_cast<ABI::Microsoft::UI::Xaml::Controls::Primitives::IButtonBaseStatics*>(this);
    }
    else
    {
        RRETURN(ctl::BetterAggregableAbstractCoreObjectActivationFactory::QueryInterfaceImpl(iid, ppObject));
    }

    AddRefOuter();
    RRETURN(S_OK);
}


// Factory methods.
IFACEMETHODIMP DirectUI::ButtonBaseFactory::CreateInstance(_In_opt_ IInspectable* pOuter, _Outptr_ IInspectable** ppInner, _Outptr_ ABI::Microsoft::UI::Xaml::Controls::Primitives::IButtonBase** ppInstance)
{

#if DBG
    // We play some games with reinterpret_cast and assuming that the GUID type table is accurate - which is somewhat sketchy, but
    // really good for binary size.  This code is a sanity check that the games we play are ok.
    const GUID uuidofGUID = __uuidof(ABI::Microsoft::UI::Xaml::Controls::Primitives::IButtonBase);
    const GUID metadataAPIGUID = MetadataAPI::GetClassInfoByIndex(GetTypeIndex())->GetGuid();
    const KnownTypeIndex typeIndex = GetTypeIndex();

    if(uuidofGUID != metadataAPIGUID)
    {
        XAML_FAIL_FAST();
    }
#endif

    // Can't just IFC(_RETURN) this because for some validate calls (those with multiple template parameters), the
    // preprocessor gets confused at the "," in the template type-list before the function's opening parenthesis.
    // So we'll use IFC_RETURN syntax with a local hr variable, kind of weirdly.
    const HRESULT hr = ctl::ValidateFactoryCreateInstanceWithBetterAggregableAbstractCoreObjectActivationFactory(pOuter, ppInner, reinterpret_cast<IUnknown**>(ppInstance), GetTypeIndex(), false /*isFreeThreaded*/);
    IFC_RETURN(hr);
    return S_OK;
}

// Dependency properties.
IFACEMETHODIMP DirectUI::ButtonBaseFactory::get_ClickModeProperty(_Out_ ABI::Microsoft::UI::Xaml::IDependencyProperty** ppValue)
{
    RRETURN(MetadataAPI::GetIDependencyProperty(KnownPropertyIndex::ButtonBase_ClickMode, ppValue));
}
IFACEMETHODIMP DirectUI::ButtonBaseFactory::get_IsPointerOverProperty(_Out_ ABI::Microsoft::UI::Xaml::IDependencyProperty** ppValue)
{
    RRETURN(MetadataAPI::GetIDependencyProperty(KnownPropertyIndex::ButtonBase_IsPointerOver, ppValue));
}
IFACEMETHODIMP DirectUI::ButtonBaseFactory::get_IsPressedProperty(_Out_ ABI::Microsoft::UI::Xaml::IDependencyProperty** ppValue)
{
    RRETURN(MetadataAPI::GetIDependencyProperty(KnownPropertyIndex::ButtonBase_IsPressed, ppValue));
}
IFACEMETHODIMP DirectUI::ButtonBaseFactory::get_CommandProperty(_Out_ ABI::Microsoft::UI::Xaml::IDependencyProperty** ppValue)
{
    RRETURN(MetadataAPI::GetIDependencyProperty(KnownPropertyIndex::ButtonBase_Command, ppValue));
}
IFACEMETHODIMP DirectUI::ButtonBaseFactory::get_CommandParameterProperty(_Out_ ABI::Microsoft::UI::Xaml::IDependencyProperty** ppValue)
{
    RRETURN(MetadataAPI::GetIDependencyProperty(KnownPropertyIndex::ButtonBase_CommandParameter, ppValue));
}

// Attached properties.

// Static properties.

// Static methods.

namespace DirectUI
{
    _Check_return_ IActivationFactory* CreateActivationFactory_ButtonBase()
    {
        RRETURN(ctl::ActivationFactoryCreator<ButtonBaseFactory>::CreateActivationFactory());
    }
}