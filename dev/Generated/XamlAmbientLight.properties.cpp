// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

// DO NOT EDIT! This file was generated by CustomTasks.DependencyPropertyCodeGen
#include "pch.h"
#include "common.h"
#include "XamlAmbientLight.h"

CppWinRTActivatableClassWithDPFactory(XamlAmbientLight)

GlobalDependencyProperty XamlAmbientLightProperties::s_ColorProperty{ nullptr };
GlobalDependencyProperty XamlAmbientLightProperties::s_IsTargetProperty{ nullptr };

XamlAmbientLightProperties::XamlAmbientLightProperties()
{
    EnsureProperties();
}

void XamlAmbientLightProperties::EnsureProperties()
{
    if (!s_ColorProperty)
    {
        s_ColorProperty =
            InitializeDependencyProperty(
                L"Color",
                winrt::name_of<winrt::Color>(),
                winrt::name_of<winrt::XamlAmbientLight>(),
                false /* isAttached */,
                ValueHelper<winrt::Color>::BoxValueIfNecessary({ 255, 255, 255, 255 }),
                winrt::PropertyChangedCallback(&OnColorPropertyChanged));
    }
    if (!s_IsTargetProperty)
    {
        s_IsTargetProperty =
            InitializeDependencyProperty(
                L"IsTarget",
                winrt::name_of<bool>(),
                winrt::name_of<winrt::XamlAmbientLight>(),
                true /* isAttached */,
                ValueHelper<bool>::BoxedDefaultValue(),
                &XamlAmbientLight::OnIsTargetPropertyChanged);
    }
}

void XamlAmbientLightProperties::ClearProperties()
{
    s_ColorProperty = nullptr;
    s_IsTargetProperty = nullptr;
}

void XamlAmbientLightProperties::OnColorPropertyChanged(
    winrt::DependencyObject const& sender,
    winrt::DependencyPropertyChangedEventArgs const& args)
{
    auto owner = sender.as<winrt::XamlAmbientLight>();
    winrt::get_self<XamlAmbientLight>(owner)->OnColorPropertyChanged(args);
}

void XamlAmbientLightProperties::Color(winrt::Color const& value)
{
    static_cast<XamlAmbientLight*>(this)->SetValue(s_ColorProperty, ValueHelper<winrt::Color>::BoxValueIfNecessary(value));
}

winrt::Color XamlAmbientLightProperties::Color()
{
    return ValueHelper<winrt::Color>::CastOrUnbox(static_cast<XamlAmbientLight*>(this)->GetValue(s_ColorProperty));
}


void XamlAmbientLightProperties::SetIsTarget(winrt::DependencyObject const& target, bool value)
{
    target.SetValue(s_IsTargetProperty, ValueHelper<bool>::BoxValueIfNecessary(value));
}

bool XamlAmbientLightProperties::GetIsTarget(winrt::DependencyObject const& target)
{
    return ValueHelper<bool>::CastOrUnbox(target.GetValue(s_IsTargetProperty));
}
