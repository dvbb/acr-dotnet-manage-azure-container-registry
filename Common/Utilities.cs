// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Azure.Core;
using Azure.ResourceManager.Compute;
using Azure.ResourceManager.Compute.Models;
using Azure.ResourceManager.Network;
using Azure.ResourceManager.Network.Models;
using Azure.ResourceManager.Resources;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Azure.ResourceManager.Samples.Common
{
    public static class Utilities
    {
        private const string dummySSHKey = "ssh-rsa AAAAB3NzaC1yc2EAAAADAQABAAABAQC+wWK73dCr+jgQOAxNsHAnNNNMEMWOHYEccp6wJm2gotpr9katuF/ZAdou5AaW1C61slRkHRkpRRX9FA9CYBiitZgvCCz+3nWNN7l/Up54Zps/pHWGZLHNJZRYyAB6j5yVLMVHIHriY49d/GZTZVNB8GoJv9Gakwc/fuEZYYl4YDFiGMBP///TzlI4jhiJzjKnEvqPFki5p2ZRJqcbCiF4pJrxUQR/RXqVFQdbRLZgYfJ8xGB878RENq3yQ39d8dVOkq4edbkzwcUmwwwkYVPIoDGsYLaRHnG+To7FvMeyO7xDVQkMKzopTQV8AuKpyvpqu0a9pWOMaiCyDytO7GGN you@me.com";
        public static Action<string> LoggerMethod { get; set; }
        public static Func<string> PauseMethod { get; set; }
        public static string ProjectPath { get; set; }
        private static Random _random => new Random();

        static Utilities()
        {
            LoggerMethod = Console.WriteLine;
            PauseMethod = Console.ReadLine;
            ProjectPath = ".";
        }

        public static void Log(string message)
        {
            LoggerMethod.Invoke(message);
        }

        public static void Log(object obj)
        {
            if (obj != null)
            {
                LoggerMethod.Invoke(obj.ToString());
            }
            else
            {
                LoggerMethod.Invoke("(null)");
            }
        }

        public static void Log()
        {
            Utilities.Log("");
        }

        public static string ReadLine() => PauseMethod.Invoke();

        public static string CreateRandomName(string namePrefix) => $"{namePrefix}{_random.Next(9999)}";

        public static string CreatePassword() => "azure12345QWE!";

        public static string CreateUsername() => "tirekicker";


        public static string CheckAddress(string url, IDictionary<string, string> headers = null)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(300);
                    if (headers != null)
                    {
                        foreach (var header in headers)
                        {
                            client.DefaultRequestHeaders.Add(header.Key, header.Value);
                        }
                    }
                    return $"Ping: {url}: {client.GetAsync(url).Result.StatusCode}";
                }
            }
            catch (Exception ex)
            {
                Utilities.Log(ex);
                return ex.ToString();
            }
        }

        static async Task<VirtualMachineResource> CreateDefaultVirtualMachine(ResourceGroupResource resourceGroup, string vmName)
        {
            string adminUsername = Utilities.CreateUsername();
            string vnetName = Utilities.CreateRandomName("vnet");
            string publicIPName = Utilities.CreateRandomName("publicIP");
            string nicName = Utilities.CreateRandomName("networkInterface");

            // Create Network
            VirtualNetworkData vnetInput = new VirtualNetworkData()
            {
                Location = resourceGroup.Data.Location,
                AddressPrefixes = { "10.10.0.0/16" },
                Subnets =
                {
                    new SubnetData() { Name = "subnet1", AddressPrefix = "10.10.1.0/24" },
                    new SubnetData() { Name = "subnet1", AddressPrefix = "10.10.2.0/24" }
                }
            };
            var vnetLro = await resourceGroup.GetVirtualNetworks().CreateOrUpdateAsync(WaitUntil.Completed, vnetName, vnetInput);
            VirtualNetworkResource vnet = vnetLro.Value;

            // Create a public IP
            var publicIPInput = new PublicIPAddressData()
            {
                Location = resourceGroup.Data.Location,
                PublicIPAllocationMethod = NetworkIPAllocationMethod.Dynamic,
            };
            var publicIP = await resourceGroup.GetPublicIPAddresses().CreateOrUpdateAsync(WaitUntil.Completed, publicIPName, publicIPInput);

            // Get subnet id
            var subnetLro = await vnet.GetSubnets().GetAsync("default");
            var subnetId = subnetLro.Value.Data.Id;

            var subnetInput = new NetworkInterfaceData()
            {
                Location = resourceGroup.Data.Location,
                IPConfigurations =
                {
                    new NetworkInterfaceIPConfigurationData()
                    {
                        Name = "default-config",
                        PrivateIPAllocationMethod = NetworkIPAllocationMethod.Dynamic,
                        PublicIPAddress = new PublicIPAddressData()
                        {
                            Id = publicIP.Value.Id
                        },
                        Subnet = new SubnetData()
                        {
                            Id = subnetId
                        }
                    }
                }
            };
            var networkInterfaceLro = await resourceGroup.GetNetworkInterfaces().CreateOrUpdateAsync(WaitUntil.Completed, nicName, subnetInput);
            NetworkInterfaceResource networkInterface = networkInterfaceLro.Value;

            VirtualMachineCollection vmCollection = resourceGroup.GetVirtualMachines();
            VirtualMachineData vmInput = new VirtualMachineData(resourceGroup.Data.Location)
            {
                HardwareProfile = new VirtualMachineHardwareProfile()
                {
                    VmSize = VirtualMachineSizeType.StandardF2
                },
                OSProfile = new VirtualMachineOSProfile()
                {
                    AdminUsername = adminUsername,
                    ComputerName = vmName,
                    LinuxConfiguration = new LinuxConfiguration()
                    {
                        DisablePasswordAuthentication = true,
                        SshPublicKeys = {
                            new SshPublicKeyConfiguration()
                            {
                                Path = $"/home/{adminUsername}/.ssh/authorized_keys",
                                KeyData = dummySSHKey,
                            }
                        }
                    }
                },
                NetworkProfile = new VirtualMachineNetworkProfile()
                {
                    NetworkInterfaces =
                    {
                        new VirtualMachineNetworkInterfaceReference()
                        {
                            Id = networkInterface.Id,
                            Primary = true,
                        }
                    }
                },
                StorageProfile = new VirtualMachineStorageProfile()
                {
                    OSDisk = new VirtualMachineOSDisk(DiskCreateOptionType.FromImage)
                    {
                        OSType = SupportedOperatingSystemType.Linux,
                        Caching = CachingType.ReadWrite,
                        ManagedDisk = new VirtualMachineManagedDisk()
                        {
                            StorageAccountType = StorageAccountType.StandardLrs
                        }
                    },
                    ImageReference = new ImageReference()
                    {
                        Publisher = "Canonical",
                        Offer = "UbuntuServer",
                        Sku = "16.04-LTS",
                        Version = "latest",
                    }
                }
            };
            ArmOperation<VirtualMachineResource> vmLro = await vmCollection.CreateOrUpdateAsync(WaitUntil.Completed, vmName, vmInput);
            return vmLro.Value;
        }
    }
}