# Subscription and Payment Integration Task List

This document captures the outstanding work required to integrate subscription
management and payment processing features across the backend and frontend of
the application. The requirements are organised by domain to simplify
prioritisation and implementation.

## Backend

### Payment Controller
- Implement a `POST` endpoint to receive notification callbacks from YooMoney.
- Validate the notification signature/secret key and supported payment types.
- Process the "3 days", "month", and "year" tariffs while linking the payment
to the corresponding user account.

### Subscription Model and Limit Calculation
- Create subscription and tariff entities defining price, duration, and the
  available functionality.
- Persist subscription start/end dates along with the payment source metadata.
- Encapsulate the free-tier limitation logic in a dedicated service providing:
  - Three YouTube recognitions per day.
  - Two transcriptions per month.
- Schedule a cron/background job to deactivate expired subscriptions.

### Integration with Existing Functionality
- Update the YouTube recognition and transcription services to enforce limits
  and verify subscription status before processing requests.
- Return informative responses with limitation messages and payment links when
  no active subscription is found.
- Log each usage attempt to support limit tracking.

### User Profile
- Add an API method returning subscription status, expiration date, and payment
  history.
- Allow administrators to extend or terminate a subscription manually.

### Admin Panel
- Provide a form to record manual payments, including amount and subscription
  expiration date.
- Display the list of subscriptions with status filters and notifications for
  upcoming expirations.

## Frontend

### Payment Page
- Display subscription options (3 days, month, year) with a button that routes
  the user to the YooMoney payment flow.
- Handle backend responses to surface success and error payment states.

### User Profile
- Show the current subscription status, expiration date, and payment history.
- Present a notification and payment call-to-action when the subscription has
  expired.

### YouTube Recognition and Transcription Interfaces
- When limits are exceeded, surface a subscription requirement message with a
  link to the payment page.
- Display a "Unlimited" status when the user holds an active subscription.

### Admin Interface
- Offer tooling for administrators to manage subscriptions, including manual
  payment entry, end-date adjustments, and viewing usage logs.
