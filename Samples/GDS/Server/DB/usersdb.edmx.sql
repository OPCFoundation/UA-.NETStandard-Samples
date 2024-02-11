
-- --------------------------------------------------
-- Entity Designer DDL Script for SQL Server 2005, 2008, 2012 and Azure
-- --------------------------------------------------
-- Date Created: 02/07/2024 14:25:58
-- Generated from EDMX file: usersdb.edmx
-- --------------------------------------------------

SET QUOTED_IDENTIFIER OFF;
GO
USE [usersdb];
GO
IF SCHEMA_ID(N'dbo') IS NULL EXECUTE(N'CREATE SCHEMA [dbo]');
GO

-- --------------------------------------------------
-- Dropping existing FOREIGN KEY constraints
-- --------------------------------------------------

IF OBJECT_ID(N'[dbo].[FK_UserSqlRole]', 'F') IS NOT NULL
    ALTER TABLE [dbo].[SqlRoleSet] DROP CONSTRAINT [FK_UserSqlRole];
GO

-- --------------------------------------------------
-- Dropping existing tables
-- --------------------------------------------------

IF OBJECT_ID(N'[dbo].[UserSet]', 'U') IS NOT NULL
    DROP TABLE [dbo].[UserSet];
GO
IF OBJECT_ID(N'[dbo].[SqlRoleSet]', 'U') IS NOT NULL
    DROP TABLE [dbo].[SqlRoleSet];
GO

-- --------------------------------------------------
-- Creating all tables
-- --------------------------------------------------

-- Creating table 'UserSet'
CREATE TABLE [dbo].[UserSet] (
    [ID] uniqueidentifier  NOT NULL,
    [UserName] nvarchar(max)  NOT NULL,
    [Hash] nvarchar(max)  NOT NULL
);
GO

-- Creating table 'SqlRoleSet'
CREATE TABLE [dbo].[SqlRoleSet] (
    [Id] uniqueidentifier  NOT NULL,
    [RoleId] uniqueidentifier  NULL,
    [Name] nvarchar(max)  NOT NULL,
    [UserID] uniqueidentifier  NOT NULL
);
GO

-- --------------------------------------------------
-- Creating all PRIMARY KEY constraints
-- --------------------------------------------------

-- Creating primary key on [ID] in table 'UserSet'
ALTER TABLE [dbo].[UserSet]
ADD CONSTRAINT [PK_UserSet]
    PRIMARY KEY CLUSTERED ([ID] ASC);
GO

-- Creating primary key on [Id] in table 'SqlRoleSet'
ALTER TABLE [dbo].[SqlRoleSet]
ADD CONSTRAINT [PK_SqlRoleSet]
    PRIMARY KEY CLUSTERED ([Id] ASC);
GO

-- --------------------------------------------------
-- Creating all FOREIGN KEY constraints
-- --------------------------------------------------

-- Creating foreign key on [UserID] in table 'SqlRoleSet'
ALTER TABLE [dbo].[SqlRoleSet]
ADD CONSTRAINT [FK_UserSqlRole]
    FOREIGN KEY ([UserID])
    REFERENCES [dbo].[UserSet]
        ([ID])
    ON DELETE CASCADE ON UPDATE NO ACTION;
GO

-- Creating non-clustered index for FOREIGN KEY 'FK_UserSqlRole'
CREATE INDEX [IX_FK_UserSqlRole]
ON [dbo].[SqlRoleSet]
    ([UserID]);
GO

-- --------------------------------------------------
-- Script has ended
-- --------------------------------------------------
