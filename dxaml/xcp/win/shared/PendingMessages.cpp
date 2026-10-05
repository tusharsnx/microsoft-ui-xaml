// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

#include "precomp.h"

#include <XcpWindow.h>

//------------------------------------------------------------------------
//
//  Method: CPendingMessage constructor
//
//------------------------------------------------------------------------

CPendingMessage::CPendingMessage(
    _In_ CXcpDispatcher * pDispatcher,
    _In_ UINT uMsg,
    _In_ WPARAM wParam,
    _In_ LPARAM lParam)
{
    XCP_STRONG(&m_pDispatcher);
    m_pDispatcher = pDispatcher;
    m_pDispatcher->AddRef();

    m_uMsg = uMsg;
    m_wParam = wParam;
    m_lParam = lParam;
    m_bPosted = FALSE;
}

//------------------------------------------------------------------------
//
//  Method: CPendingMessage destructor
//
//------------------------------------------------------------------------

CPendingMessage::~CPendingMessage()
{
    // If message was posted, free corresponding resources
    if (m_bPosted)
    {
        m_pDispatcher->ReleaseMessageResources(
                        m_uMsg,
                        m_lParam);
    }

    ReleaseInterface(m_pDispatcher);
}

//------------------------------------------------------------------------
//
//  Method: CPendingMessage PostMessage
//
//  Synopsis: Post message to dispatcher's queue
//------------------------------------------------------------------------

_Check_return_
HRESULT CPendingMessage::PostMessage()
{
    HRESULT hr = S_OK;

    // Posted already?
    ASSERT(!m_bPosted);

    IFCEXPECT(SUCCEEDED(m_pDispatcher->QueueDeferredInvoke(m_uMsg, m_wParam, m_lParam)));

    m_bPosted = TRUE;

Cleanup:
    return hr;
}

//------------------------------------------------------------------------
//
//  Method: CPendingMessages factory method
//
//------------------------------------------------------------------------

_Check_return_ HRESULT CPendingMessages::Create(_In_ CXcpDispatcher* pDispatcher, _Outptr_ CPendingMessages** ppPendingMessages)
{
    HRESULT hr = S_OK;

    CPendingMessages* pMessages = NULL;

    pMessages = new CPendingMessages(pDispatcher);

    *ppPendingMessages = pMessages;

    RRETURN(hr);//RRETURN_REMOVAL
}

//------------------------------------------------------------------------
//
//  Method: CPendingMessages constructor
//
//------------------------------------------------------------------------

CPendingMessages::CPendingMessages(
    _In_ CXcpDispatcher * pDispatcher)
{
    XCP_STRONG(&m_pDispatcher);
    m_pDispatcher = pDispatcher;
    m_pDispatcher->AddRef();
}

//------------------------------------------------------------------------
//
//  Method: CPendingMessages destructor
//
//------------------------------------------------------------------------

CPendingMessages::~CPendingMessages()
{
    ReleaseInterface(m_pDispatcher);
}

//------------------------------------------------------------------------
//
//  Method: CPendingMessages PostMessage
//
//  Synopsis: Post message with associated resource to dispatcher's queue
//      and add message to list of pending messages.
//
//      Code to release the resource must be added to
//      CXcpDispatcher::ReleaseMessageResources.
//------------------------------------------------------------------------

_Check_return_
HRESULT CPendingMessages::PostMessage(
    _In_ UINT uMsg,
    _In_ WPARAM wParam,
    _In_ LPARAM lParam)
{
    HRESULT hr = S_OK;
    CPendingMessageList::iterator pendingMessageIt = m_messageList.end();
    XINT32 bAddedToList = FALSE;

    // Pending messages with resources don't use wParam
    ASSERT(wParam == 0);

    // Add to list of pending messages
    // Scope the lock
    {
        auto Lock = m_CS.lock();

        pendingMessageIt = m_messageList.emplace(m_messageList.end(), m_pDispatcher, uMsg, 0, lParam);

        bAddedToList = TRUE;

        // Create a copy of the iterator on the heap and store its address in wParam.
        auto pPendingMessageIt = std::make_unique<CPendingMessageList::iterator>(pendingMessageIt);
        pendingMessageIt->m_wParam = reinterpret_cast<WPARAM>(pPendingMessageIt.get());

        // Post message to dispatcher's queue. This must be done
        // before unlocking the message queue as otherwise it
        // could be removed and cleaned up if a ReleaseResources
        // call is made from another thread.
        IFC(pendingMessageIt->PostMessage());

        // The message was posted succesfully. Release the pointer without freeing it.
        pPendingMessageIt.release();
    }

Cleanup:
    if (FAILED(hr))
    {
        if (bAddedToList)
        {
            auto Lock = m_CS.lock();

            IGNOREHR(m_messageList.erase(pendingMessageIt));
        }
    }

    return hr;
}

//------------------------------------------------------------------------
//
//  Method: CPendingMessages ReleaseResource
//
//  Synopsis: Release resource associated with message
//------------------------------------------------------------------------

_Check_return_
HRESULT CPendingMessages::ReleaseResource(
    _In_ UINT uMsg,
    _In_ WPARAM wParam,
    _In_ LPARAM lParam)
{
    HRESULT hr = S_OK;

    // wParam holds a reference to this message's resource. Some messages don't have any resources,
    // in that case, wParam would be 0.
    if (wParam != 0)
    {
        auto Lock = m_CS.lock();

        // Take ownership of the heap-allocated iterator from wParam so it is deleted after erasing
        std::unique_ptr<CPendingMessageList::iterator> pItPendingMessage(reinterpret_cast<CPendingMessageList::iterator*>(wParam));

        if (pItPendingMessage)
        {
            const auto itPendingMessage = *pItPendingMessage;

            // Assert we are deleting the correct message.
            ASSERT(itPendingMessage->m_uMsg == uMsg &&
                   itPendingMessage->m_wParam == wParam &&
                   itPendingMessage->m_lParam == lParam);

            // Resource is freed in message's destructor, when
            // message is removed from list
            m_messageList.erase(itPendingMessage);
        }
    }

    return hr;
}

//------------------------------------------------------------------------
//
//  Method: CPendingMessages::ReleaseResources
//
//  Synopsis: Release resources for all messages
//------------------------------------------------------------------------

void CPendingMessages::ReleaseResources()
{
    auto Lock = m_CS.lock();

    // Delete message references within messages
    for (const auto& msg : m_messageList)
    {
        delete reinterpret_cast<CPendingMessages::CPendingMessageList::iterator*>(msg.m_wParam);
    }

    // Resources are released in each message's destructor
    m_messageList.clear();
}

