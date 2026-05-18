SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [cust].[Order](
	[OrderId] [int] IDENTITY(1,1) NOT NULL,
	[CustomerId] [int] NOT NULL,
	[Status] [nvarchar](20) NOT NULL,
	[AmountCents] [int] NOT NULL,
	[SquareOrderId] [nvarchar](64) NULL,
	[SquarePaymentLinkId] [nvarchar](64) NULL,
	[SquarePaymentLinkUrl] [nvarchar](500) NULL,
	[ShipLabel] [nvarchar](50) NULL,
	[ShipStreet1] [nvarchar](200) NULL,
	[ShipStreet2] [nvarchar](200) NULL,
	[ShipCity] [nvarchar](100) NULL,
	[ShipState] [nvarchar](50) NULL,
	[ShipZip] [nvarchar](20) NULL,
	[ShipCountry] [nvarchar](2) NULL,
	[CustomerNotes] [nvarchar](max) NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
	[PaidAt] [datetime2](7) NULL,
	[CancelledAt] [datetime2](7) NULL
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
ALTER TABLE [cust].[Order] ADD  CONSTRAINT [PK_Order] PRIMARY KEY CLUSTERED 
(
	[OrderId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
CREATE NONCLUSTERED INDEX [IX_Order_Customer_CreatedAt] ON [cust].[Order]
(
	[CustomerId] ASC,
	[CreatedAt] DESC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
CREATE UNIQUE NONCLUSTERED INDEX [UX_Order_SquareOrderId] ON [cust].[Order]
(
	[SquareOrderId] ASC
)
WHERE ([SquareOrderId] IS NOT NULL)
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
ALTER TABLE [cust].[Order] ADD  CONSTRAINT [DF_Order_ShipCountry]  DEFAULT ('US') FOR [ShipCountry]
GO
ALTER TABLE [cust].[Order] ADD  CONSTRAINT [DF_Order_CreatedAt]  DEFAULT (sysutcdatetime()) FOR [CreatedAt]
GO
ALTER TABLE [cust].[Order]  WITH CHECK ADD  CONSTRAINT [FK_Order_Customer] FOREIGN KEY([CustomerId])
REFERENCES [cust].[Customer] ([CustomerId])
GO
ALTER TABLE [cust].[Order] CHECK CONSTRAINT [FK_Order_Customer]
GO
ALTER TABLE [cust].[Order]  WITH CHECK ADD  CONSTRAINT [CK_Order_Amount] CHECK  (([AmountCents]>=(0)))
GO
ALTER TABLE [cust].[Order] CHECK CONSTRAINT [CK_Order_Amount]
GO
ALTER TABLE [cust].[Order]  WITH CHECK ADD  CONSTRAINT [CK_Order_Status] CHECK  (([Status]='Failed' OR [Status]='Cancelled' OR [Status]='Paid' OR [Status]='PaymentPending'))
GO
ALTER TABLE [cust].[Order] CHECK CONSTRAINT [CK_Order_Status]
GO
