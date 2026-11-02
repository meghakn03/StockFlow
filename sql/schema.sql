CREATE TABLE [AuditEvents] (
    [Id] bigint NOT NULL IDENTITY,
    [At] datetime2 NOT NULL,
    [Actor] nvarchar(160) NOT NULL,
    [Action] nvarchar(80) NOT NULL,
    [Detail] nvarchar(1000) NOT NULL,
    CONSTRAINT [PK_AuditEvents] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [Customers] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(160) NOT NULL,
    [Email] nvarchar(254) NOT NULL,
    [Address] nvarchar(300) NOT NULL,
    CONSTRAINT [PK_Customers] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [Products] (
    [Id] int NOT NULL IDENTITY,
    [Sku] nvarchar(40) NOT NULL,
    [Name] nvarchar(160) NOT NULL,
    [Category] nvarchar(80) NOT NULL,
    [Price] decimal(18,2) NOT NULL,
    [OnHand] int NOT NULL,
    [Reserved] int NOT NULL,
    [ReorderLevel] int NOT NULL,
    [Active] bit NOT NULL,
    CONSTRAINT [PK_Products] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Product_Price] CHECK ([Price] >= 0 AND [ReorderLevel] >= 0),
    CONSTRAINT [CK_Product_Stock] CHECK ([OnHand] >= 0 AND [Reserved] >= 0 AND [Reserved] <= [OnHand])
);
GO


CREATE TABLE [Users] (
    [Id] int NOT NULL IDENTITY,
    [Email] nvarchar(160) NOT NULL,
    [PasswordHash] nvarchar(max) NOT NULL,
    [Role] nvarchar(20) NOT NULL,
    CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [Orders] (
    [Id] int NOT NULL IDENTITY,
    [CustomerId] int NOT NULL,
    [Status] nvarchar(20) NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_Orders] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Order_Status] CHECK ([Status] IN ('Draft','Confirmed','Shipped','Cancelled')),
    CONSTRAINT [FK_Orders_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers] ([Id]) ON DELETE NO ACTION
);
GO


CREATE TABLE [OrderLines] (
    [Id] int NOT NULL IDENTITY,
    [OrderId] int NOT NULL,
    [ProductId] int NOT NULL,
    [Quantity] int NOT NULL,
    [UnitPrice] decimal(18,2) NOT NULL,
    CONSTRAINT [PK_OrderLines] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Line_Quantity] CHECK ([Quantity] > 0 AND [UnitPrice] >= 0),
    CONSTRAINT [FK_OrderLines_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_OrderLines_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE NO ACTION
);
GO


CREATE UNIQUE INDEX [IX_Customers_Email] ON [Customers] ([Email]);
GO


CREATE UNIQUE INDEX [IX_OrderLines_OrderId_ProductId] ON [OrderLines] ([OrderId], [ProductId]);
GO


CREATE INDEX [IX_OrderLines_ProductId] ON [OrderLines] ([ProductId]);
GO


CREATE INDEX [IX_Orders_CustomerId] ON [Orders] ([CustomerId]);
GO


CREATE INDEX [IX_Orders_Status_CreatedAt] ON [Orders] ([Status], [CreatedAt]);
GO


CREATE UNIQUE INDEX [IX_Products_Sku] ON [Products] ([Sku]);
GO


CREATE UNIQUE INDEX [IX_Users_Email] ON [Users] ([Email]);
GO


