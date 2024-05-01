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

#include "CModernCollectionBasePanel.g.h"
#include <DeclareMacros.h>
#include <Indexes.g.h>
#include <minxcptypes.h>

class CCalendarPanel : public CModernCollectionBasePanel
{
protected:
    CCalendarPanel(_In_ CCoreServices *pCore)
        : CModernCollectionBasePanel(pCore)
    {
        SetIsCustomType();
    }

    ~CCalendarPanel() override = default;

public:
    DECLARE_CREATE(CCalendarPanel);

    KnownTypeIndex GetTypeIndex() const override
    {
        return KnownTypeIndex::CalendarPanel;
    }

    XUINT32 ParticipatesInManagedTreeInternal() override
    {
        return PARTICIPATES_IN_MANAGED_TREE;
    }
};