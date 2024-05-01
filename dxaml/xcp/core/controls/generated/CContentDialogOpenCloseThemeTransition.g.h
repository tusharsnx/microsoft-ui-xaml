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

#include "LayoutTransition.h"
#include <DeclareMacros.h>
#include <Indexes.g.h>
#include <minxcptypes.h>

class CContentDialogOpenCloseThemeTransition : public CTransition
{
protected:
    CContentDialogOpenCloseThemeTransition(_In_ CCoreServices *pCore)
        : CTransition(pCore)
    {
        SetIsCustomType();
    }

    ~CContentDialogOpenCloseThemeTransition() override = default;

public:
    DECLARE_CREATE(CContentDialogOpenCloseThemeTransition);

    KnownTypeIndex GetTypeIndex() const override
    {
        return KnownTypeIndex::ContentDialogOpenCloseThemeTransition;
    }

    XUINT32 ParticipatesInManagedTreeInternal() override
    {
        return PARTICIPATES_IN_MANAGED_TREE;
    }
};