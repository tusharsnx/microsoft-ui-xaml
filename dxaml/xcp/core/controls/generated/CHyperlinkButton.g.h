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

#include "CButtonBase.g.h"
#include <DeclareMacros.h>
#include <Indexes.g.h>
#include <minxcptypes.h>

class CHyperlinkButton : public CButtonBase
{
protected:
    CHyperlinkButton(_In_ CCoreServices *pCore)
        : CButtonBase(pCore)
    {
        SetIsCustomType();
    }

    ~CHyperlinkButton() override = default;

public:
    DECLARE_CREATE(CHyperlinkButton);

    KnownTypeIndex GetTypeIndex() const override
    {
        return KnownTypeIndex::HyperlinkButton;
    }

    XUINT32 ParticipatesInManagedTreeInternal() override
    {
        return PARTICIPATES_IN_MANAGED_TREE;
    }
};