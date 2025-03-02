# FinanceTab Design Document

## Overview
The FinanceTab is a comprehensive financial management interface within Universa that provides users with the ability to track multiple accounts, view transaction history, and analyze investments. It integrates with the AI Chatsidebar through a specialized FinanceChain for intelligent financial analysis and real-time market data.

## Core Features

### 1. Account Management
- Support for multiple account types:
  - Checking/Savings accounts
  - Investment accounts (IRA, 401k, etc.)
  - Credit cards
  - Loans
  - Custom account types
- Account grouping and categorization
- Account balance tracking and history
- Account-specific metadata (account numbers, institutions, etc.)

### 2. Ledger View
- Multi-account transaction ledger
- Columns:
  - Date
  - Account
  - Description
  - Category
  - Amount (Debit/Credit)
  - Running Balance
  - Tags
- Filtering capabilities:
  - By account(s)
  - By date range
  - By category
  - By amount range
  - By tags
- Sorting by any column
- Export functionality (CSV, Excel)

### 3. Account Detail View
- Detailed view when selecting an account:
  - Account summary
  - Balance history graph
  - Recent transactions
  - Account-specific metrics
- Investment Account Features:
  - Holdings breakdown
  - Asset allocation
  - Performance metrics
  - Individual security details
  - Investment history

### 4. Navigation Structure
- Left sidebar navigation tree:
  ```
  📊 Overview
  └── 💰 Accounts
      ├── 🏦 Bank Accounts
      │   ├── Checking
      │   └── Savings
      ├── 💳 Credit Cards
      ├── 📈 Investments
      │   ├── IRA
      │   └── 401k
      └── 💵 Loans
  ```
- Quick filters for account types
- Drag-and-drop support for account organization

### 5. AI Integration (FinanceChain)
- Natural language queries about finances
- Real-time market data integration
- Financial analysis capabilities:
  - Portfolio analysis
  - Investment recommendations
  - Risk assessment
  - Market trends
- API integrations:
  - Yahoo Finance
  - Alpha Vantage
  - Other financial data providers

## Technical Implementation

### Data Models

```csharp
public class FinancialAccount
{
    public string Id { get; set; }
    public string Name { get; set; }
    public AccountType Type { get; set; }
    public decimal CurrentBalance { get; set; }
    public string Institution { get; set; }
    
    // Sensitive data stored separately and encrypted
    [EncryptedStorage]
    public AccountSecureData SecureData { get; set; }
    
    public Dictionary<string, object> Metadata { get; set; }
}

public class AccountSecureData
{
    public string AccountNumber { get; set; }
    public string RoutingNumber { get; set; }
    public string AccessToken { get; set; }
}

public class Transaction
{
    public string Id { get; set; }
    public DateTime Date { get; set; }
    public string Description { get; set; }
    public decimal Amount { get; set; }
    public string Category { get; set; }
    public string AccountId { get; set; }
    public List<string> Tags { get; set; }
    public TransactionType Type { get; set; }
}

public class Investment
{
    public string Symbol { get; set; }
    public string Name { get; set; }
    public decimal Shares { get; set; }
    public decimal CurrentPrice { get; set; }
    public decimal CostBasis { get; set; }
    public AssetClass AssetClass { get; set; }
}

// File structure for data storage
public class FinanceData
{
    public List<FinancialAccount> Accounts { get; set; }
    public Dictionary<string, List<Transaction>> AccountTransactions { get; set; }
    public Dictionary<string, List<Investment>> AccountInvestments { get; set; }
}
```

### Storage Structure
```
/Finance/
  ├── accounts.json           # Basic account info
  ├── transactions.json       # Transaction history
  ├── investments.json        # Investment data
  └── secure/
      └── encrypted.dat      # Encrypted sensitive data
```

### View Structure

```xaml
<UserControl x:Class="Universa.Desktop.FinanceTab">
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="250"/>
            <ColumnDefinition Width="Auto"/>
            <ColumnDefinition Width="*"/>
        </Grid.ColumnDefinitions>

        <!-- Navigation Tree -->
        <TreeView x:Name="NavigationTree" Grid.Column="0"/>

        <!-- Splitter -->
        <GridSplitter Grid.Column="1"/>

        <!-- Content Area -->
        <Grid Grid.Column="2">
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="*"/>
            </Grid.RowDefinitions>

            <!-- Toolbar/Filters -->
            <ToolBar Grid.Row="0"/>

            <!-- Content (Ledger/Account Details) -->
            <ContentControl Grid.Row="1"/>
        </Grid>
    </Grid>
</UserControl>
```

### Services

1. **FinanceService**
   - Account management
   - Transaction processing
   - File-based data persistence
   - Investment tracking
   - Encryption handling for sensitive data

2. **MarketDataService**
   - Real-time market data
   - Historical price data
   - Company information
   - Financial news

3. **FinanceChain**
   - Natural language processing
   - Financial analysis
   - Market data integration
   - Investment recommendations

## Data Storage
- JSON files for general data
- Encrypted file for sensitive data (account numbers, routing numbers)
- Regular file backups
- Optional file compression for large datasets

## Security Implementation
```csharp
public class SecurityManager
{
    private readonly string _keyPath;
    private readonly byte[] _key;

    public SecurityManager()
    {
        _keyPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Universa",
            "Finance",
            "secure"
        );
        _key = GetOrCreateKey();
    }

    public void EncryptSensitiveData(AccountSecureData data, string accountId)
    {
        var json = JsonSerializer.Serialize(data);
        var encrypted = EncryptString(json);
        File.WriteAllText(
            Path.Combine(_keyPath, $"{accountId}.enc"),
            Convert.ToBase64String(encrypted)
        );
    }

    public AccountSecureData DecryptSensitiveData(string accountId)
    {
        var encryptedBase64 = File.ReadAllText(
            Path.Combine(_keyPath, $"{accountId}.enc")
        );
        var decrypted = DecryptString(Convert.FromBase64String(encryptedBase64));
        return JsonSerializer.Deserialize<AccountSecureData>(decrypted);
    }

    private byte[] EncryptString(string plainText)
    {
        // Implementation using AES encryption
    }

    private string DecryptString(byte[] cipherText)
    {
        // Implementation using AES decryption
    }
}
```

## Future Enhancements
1. Budgeting features
2. Bill payment tracking
3. Investment performance analytics
4. Tax reporting
5. Mobile companion app integration
6. Multi-currency support
7. Custom report generation
8. Automated transaction categorization using AI

## Dependencies
- Financial data provider APIs
- Chart visualization libraries
- Encryption libraries for sensitive data
- System.Text.Json for file handling 