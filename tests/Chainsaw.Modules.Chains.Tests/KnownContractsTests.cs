using System.Text;
using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.Modules.Chains.Tests;

/// <summary>
/// Chainsaw's list of known contracts on Ethereum (every entry names its public source and the day it was read, and
/// none is listed twice) and a transaction's place in its block as Blockscout's history gives it.
/// </summary>
public sealed class KnownContractsTests
{
    [Fact]
    public void EveryContractNamesItsSourceAndDayAndIsListedOnce()
    {
        Assert.NotEmpty(KnownContracts.All);
        var addresses = KnownContracts.All.Select(contract => contract.Address).ToList();
        Assert.Equal(addresses.Count, addresses.Distinct().Count());
        Assert.All(KnownContracts.All, contract =>
        {
            Assert.Equal(Uri.UriSchemeHttps, contract.Source.Scheme);
            Assert.False(string.IsNullOrWhiteSpace(contract.Name));
            Assert.Equal(new DateOnly(2026, 11, 8), contract.Checked);
            Assert.Contains(contract.Source.AbsoluteUri, contract.Provenance(), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void EveryCategoryIsOnTheList()
    {
        foreach (var category in Enum.GetValues<KnownContractCategory>())
        {
            Assert.Contains(KnownContracts.All, contract => contract.Category == category);
        }
    }

    [Fact]
    public void AContractIsFoundByItsAddressWhateverTheCaseItWasWrittenIn()
    {
        var known = KnownContracts.All[0];

        Assert.Same(known, KnownContracts.Find(EthereumAddress.Parse(known.Address.Value)));
        Assert.Same(known, KnownContracts.Find(EthereumAddress.Parse(known.Address.Checksummed)));
        Assert.Null(KnownContracts.Find(EthereumAddress.Parse("0x" + new string('7', 40))));
    }

    [Fact]
    public void AHistoryEntryGivesItsTransactionsPlaceInTheBlock()
    {
        var page = BlockscoutParser.ParseHistory(
            Encoding.UTF8.GetBytes(History("\"transactionIndex\": \"17\",")), EthereumHarness.A,
            EthereumHistory.Transactions, null, 100);
        var without = BlockscoutParser.ParseHistory(
            Encoding.UTF8.GetBytes(History(string.Empty)), EthereumHarness.A, EthereumHistory.Transactions, null, 100);

        Assert.Equal(17, Assert.Single(page.Transfers).Block!.TransactionIndex);
        Assert.Equal(17, Assert.Single(page.Fees).Block!.TransactionIndex);
        Assert.Null(Assert.Single(without.Transfers).Block!.TransactionIndex);
    }

    /// <summary>Writes a synthetic page of the address's transactions with one entry it signed.</summary>
    /// <param name="index">The entry's place in the block as JSON, or nothing.</param>
    /// <returns>The page.</returns>
    private static string History(string index) => $$"""
        {
         "status": "1",
         "message": "OK",
         "result": [
          {
           "hash": "0x{{new string('b', 64)}}",
           "blockNumber": "900",
           "blockHash": "0x{{new string('0', 61)}}384",
           {{index}}
           "timeStamp": "1793872800",
           "from": "{{EthereumHarness.A.Value}}",
           "to": "0x{{new string('5', 40)}}",
           "value": "1000",
           "gasUsed": "21000",
           "gasPrice": "1000000000",
           "isError": "0",
           "txreceipt_status": "1",
           "contractAddress": ""
          }
         ]
        }
        """;
}
