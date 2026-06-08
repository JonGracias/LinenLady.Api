BEGIN TRANSACTION;

-- Deepest children first
DELETE FROM cust.OrderItem;

-- Tables that depend on Order or Reservation
DELETE FROM cust.Message;
DELETE FROM cust.Notification;

-- Customer children
DELETE FROM cust.Reservation;
DELETE FROM cust.[Order];
DELETE FROM cust.CustomerPreference;
DELETE FROM cust.CustomerAddress;

-- Parent last
DELETE FROM cust.Customer;

-- Verify
SELECT COUNT(*) AS Customers FROM cust.Customer;
SELECT COUNT(*) AS Addresses FROM cust.CustomerAddress;
SELECT COUNT(*) AS Preferences FROM cust.CustomerPreference;
SELECT COUNT(*) AS Messages FROM cust.Message;
SELECT COUNT(*) AS Notifications FROM cust.Notification;
SELECT COUNT(*) AS Orders FROM cust.[Order];
SELECT COUNT(*) AS OrderItems FROM cust.OrderItem;
SELECT COUNT(*) AS Reservations FROM cust.Reservation;
SELECT COUNT(*) AS ContactSubmissions FROM cust.ContactSubmission;

-- If counts look right:
COMMIT TRANSACTION;

-- If anything looks wrong, use this instead of COMMIT:
-- ROLLBACK TRANSACTION;