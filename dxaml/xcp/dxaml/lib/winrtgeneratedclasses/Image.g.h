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

#include "FrameworkElement.g.h"
#include "Thickness.g.h"

#define __Image_GUID "c4d89a08-d1fc-4c96-9e52-8033e4803cf2"

namespace DirectUI
{
    class Image;
    class ImageSource;

    class __declspec(novtable) ImageGenerated:
        public DirectUI::FrameworkElement
        , public ABI::Microsoft::UI::Xaml::Controls::IImage
    {
        friend class DirectUI::Image;

        INSPECTABLE_CLASS(L"Microsoft.UI.Xaml.Controls.Image");

        BEGIN_INTERFACE_MAP(ImageGenerated, DirectUI::FrameworkElement)
            INTERFACE_ENTRY(ImageGenerated, ABI::Microsoft::UI::Xaml::Controls::IImage)
        END_INTERFACE_MAP(ImageGenerated, DirectUI::FrameworkElement)

    public:
        ImageGenerated();
        ~ImageGenerated() override;

        // Event source typedefs.
        typedef CRoutedEventSource<ABI::Microsoft::UI::Xaml::IExceptionRoutedEventHandler, IInspectable, ABI::Microsoft::UI::Xaml::IExceptionRoutedEventArgs> ImageFailedEventSourceType;
        typedef CRoutedEventSource<ABI::Microsoft::UI::Xaml::IRoutedEventHandler, IInspectable, ABI::Microsoft::UI::Xaml::IRoutedEventArgs> ImageOpenedEventSourceType;

        KnownTypeIndex GetTypeIndex() const override
        {
            return KnownTypeIndex::Image;
        }

        static XCP_FORCEINLINE KnownTypeIndex GetTypeIndexStatic()
        {
            return KnownTypeIndex::Image;
        }

        // Properties.
        IFACEMETHOD(get_NineGrid)(_Out_ ABI::Microsoft::UI::Xaml::Thickness* pValue) override;
        IFACEMETHOD(put_NineGrid)(_In_ ABI::Microsoft::UI::Xaml::Thickness value) override;
        IFACEMETHOD(get_Source)(_Outptr_result_maybenull_ ABI::Microsoft::UI::Xaml::Media::IImageSource** ppValue) override;
        IFACEMETHOD(put_Source)(_In_opt_ ABI::Microsoft::UI::Xaml::Media::IImageSource* pValue) override;
        IFACEMETHOD(get_Stretch)(_Out_ ABI::Microsoft::UI::Xaml::Media::Stretch* pValue) override;
        IFACEMETHOD(put_Stretch)(_In_ ABI::Microsoft::UI::Xaml::Media::Stretch value) override;

        // Events.
        _Check_return_ HRESULT GetImageFailedEventSourceNoRef(_Outptr_ ImageFailedEventSourceType** ppEventSource);
        IFACEMETHOD(add_ImageFailed)(_In_ ABI::Microsoft::UI::Xaml::IExceptionRoutedEventHandler* pValue, _Out_ EventRegistrationToken* pToken) override;
        IFACEMETHOD(remove_ImageFailed)(_In_ EventRegistrationToken token) override;
        _Check_return_ HRESULT GetImageOpenedEventSourceNoRef(_Outptr_ ImageOpenedEventSourceType** ppEventSource);
        IFACEMETHOD(add_ImageOpened)(_In_ ABI::Microsoft::UI::Xaml::IRoutedEventHandler* pValue, _Out_ EventRegistrationToken* pToken) override;
        IFACEMETHOD(remove_ImageOpened)(_In_ EventRegistrationToken token) override;

        // Methods.
        IFACEMETHOD(GetAlphaMask)(_Outptr_ ABI::Microsoft::UI::Composition::ICompositionBrush** ppReturnValue) override;
        IFACEMETHOD(GetAsCastingSource)(_Outptr_ ABI::Windows::Media::Casting::ICastingSource** ppReturnValue) override;


    protected:
        HRESULT QueryInterfaceImpl(_In_ REFIID iid, _Outptr_ void** ppObject) override;

    private:

        // Fields.
    };
}

#include "Image_Partial.h"

namespace DirectUI
{
    // Note that the ordering of the base types here is important - the base factory comes first, followed by all the
    // interfaces specific to this type.  By doing this, we allow every Factory's CreateInstance method to be more
    // COMDAT-folding-friendly.  Because this ensures that the first vfptr contains GetTypeIndex, it means that all
    // CreateInstance functions with the same base factory generate the same assembly instructions and thus will
    // fold together.  This is significant for binary size in Microsoft.UI.Xaml.dll so change this only with great
    // care.
    class __declspec(novtable) ImageFactory:
       public ctl::BetterCoreObjectActivationFactory
        , public ABI::Microsoft::UI::Xaml::Controls::IImageStatics
    {
        BEGIN_INTERFACE_MAP(ImageFactory, ctl::BetterCoreObjectActivationFactory)
            INTERFACE_ENTRY(ImageFactory, ABI::Microsoft::UI::Xaml::Controls::IImageStatics)
        END_INTERFACE_MAP(ImageFactory, ctl::BetterCoreObjectActivationFactory)

    public:
        // Factory methods.

        // Static properties.

        // Dependency properties.
        IFACEMETHOD(get_SourceProperty)(_Out_ ABI::Microsoft::UI::Xaml::IDependencyProperty** ppValue) override;
        IFACEMETHOD(get_StretchProperty)(_Out_ ABI::Microsoft::UI::Xaml::IDependencyProperty** ppValue) override;
        IFACEMETHOD(get_NineGridProperty)(_Out_ ABI::Microsoft::UI::Xaml::IDependencyProperty** ppValue) override;

        // Attached properties.

        // Static methods.

        // Static events.

    protected:
        HRESULT QueryInterfaceImpl(_In_ REFIID iid, _Outptr_ void** ppObject) override;

        KnownTypeIndex GetTypeIndex() const override
        {
            return KnownTypeIndex::Image;
        }


    private:

        // Customized static properties.

        // Customized static  methods.
    };
}