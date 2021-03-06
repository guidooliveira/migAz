﻿using MigAz.Azure.Interface;
using MigAz.Azure.Models;
using MigAz.Core.Interface;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using MigAz.Core.Generator;
using System.IO;
using MigAz.Core.ArmTemplate;
using System.Text;
using Newtonsoft.Json;
using System.Reflection;
using Newtonsoft.Json.Linq;
using System.Windows.Forms;

namespace MigAz.Azure.Generator.AsmToArm
{
    public class AzureGenerator : TemplateGenerator
    {
        private ITelemetryProvider _telemetryProvider;
        private ISettingsProvider _settingsProvider;
        private ExportArtifacts _ExportArtifacts;
        private List<CopyBlobDetail> _CopyBlobDetails = new List<CopyBlobDetail>();

        private AzureGenerator() : base(null, null, null, null) { } 

        public AzureGenerator(
            ISubscription sourceSubscription, 
            ISubscription targetSubscription,
            ILogProvider logProvider, 
            IStatusProvider statusProvider, 
            ITelemetryProvider telemetryProvider, 
            ISettingsProvider settingsProvider) : base(logProvider, statusProvider, sourceSubscription, targetSubscription)
        {
            _telemetryProvider = telemetryProvider;
            _settingsProvider = settingsProvider;
        }

        // Use of Treeview has been added here with aspect of transitioning full output towards this as authoritative source
        // Thought is that ExportArtifacts phases out, as it is providing limited context availability.
        public override async Task UpdateArtifacts(IExportArtifacts artifacts)
        {
            LogProvider.WriteLog("UpdateArtifacts", "Start - Execution " + this.ExecutionGuid.ToString());

            Alerts.Clear();

            _ExportArtifacts = (ExportArtifacts)artifacts;

            if (_ExportArtifacts.ResourceGroup == null)
            {
                this.AddAlert(AlertType.Error, "Target Resource Group must be provided for template generation.", _ExportArtifacts.ResourceGroup);
            }
            else
            {
                if (_ExportArtifacts.ResourceGroup.TargetLocation == null)
                {
                    this.AddAlert(AlertType.Error, "Target Resource Group Location must be provided for template generation.", _ExportArtifacts.ResourceGroup);
                }
            }

            foreach (MigrationTarget.NetworkSecurityGroup targetNetworkSecurityGroup in _ExportArtifacts.NetworkSecurityGroups)
            {
                if (targetNetworkSecurityGroup.TargetName == string.Empty)
                    this.AddAlert(AlertType.Error, "Target Name for Network Security Group must be specified.", targetNetworkSecurityGroup);
            }

            foreach (MigrationTarget.LoadBalancer targetLoadBalancer in _ExportArtifacts.LoadBalancers)
            {
                if (targetLoadBalancer.Name == string.Empty)
                    this.AddAlert(AlertType.Error, "Target Name for Load Balancer must be specified.", targetLoadBalancer);

                if (targetLoadBalancer.FrontEndIpConfigurations.Count == 0)
                {
                    this.AddAlert(AlertType.Error, "Load Balancer must have a FrontEndIpConfiguration.", targetLoadBalancer);
                }
                else
                {
                    if (targetLoadBalancer.FrontEndIpConfigurations[0].PublicIp == null &&
                        targetLoadBalancer.FrontEndIpConfigurations[0].TargetSubnet == null)
                    {
                        this.AddAlert(AlertType.Error, "Load Balancer must have either an internal Subnet association or Public IP association.", targetLoadBalancer);
                    }
                }
            }

            foreach (Azure.MigrationTarget.VirtualMachine virtualMachine in _ExportArtifacts.VirtualMachines)
            {
                if (virtualMachine.TargetName == string.Empty)
                    this.AddAlert(AlertType.Error, "Target Name for Virtual Machine '" + virtualMachine.ToString() + "' must be specified.", virtualMachine);

                if (virtualMachine.TargetAvailabilitySet == null)
                {
                    if (virtualMachine.OSVirtualHardDisk.TargetStorageAccount != null && virtualMachine.OSVirtualHardDisk.TargetStorageAccount.StorageAccountType != StorageAccountType.Premium)
                        this.AddAlert(AlertType.Warning, "Virtual Machine '" + virtualMachine.ToString() + "' is not part of an Availability Set.  OS Disk must be migrated to Azure Premium Storage to receive an Azure SLA for single server deployments.", virtualMachine);

                    foreach (Azure.MigrationTarget.Disk dataDisk in virtualMachine.DataDisks)
                    {
                        if (dataDisk.TargetStorageAccount != null && dataDisk.TargetStorageAccount.StorageAccountType != StorageAccountType.Premium)
                            this.AddAlert(AlertType.Warning, "Virtual Machine '" + virtualMachine.ToString() + "' is not part of an Availability Set.  Data Disk '" + dataDisk.ToString() + "' must be migrated to Azure Premium Storage to receive an Azure SLA for single server deployments.", virtualMachine);
                    }
                }

                foreach (Azure.MigrationTarget.NetworkInterface networkInterface in virtualMachine.NetworkInterfaces)
                {
                    foreach (Azure.MigrationTarget.NetworkInterfaceIpConfiguration ipConfiguration in networkInterface.TargetNetworkInterfaceIpConfigurations)
                    {
                        if (ipConfiguration.TargetVirtualNetwork == null)
                            this.AddAlert(AlertType.Error, "Target Virtual Network for Virtual Machine '" + virtualMachine.ToString() + "' Network Interface '" + networkInterface.ToString() + "' must be specified.", networkInterface);
                        else
                        {
                            if (ipConfiguration.TargetVirtualNetwork.GetType() == typeof(MigrationTarget.VirtualNetwork))
                            {
                                MigrationTarget.VirtualNetwork virtualMachineTargetVirtualNetwork = (MigrationTarget.VirtualNetwork)ipConfiguration.TargetVirtualNetwork;
                                bool targetVNetExists = false;

                                foreach (MigrationTarget.VirtualNetwork targetVirtualNetwork in _ExportArtifacts.VirtualNetworks)
                                {
                                    if (virtualMachineTargetVirtualNetwork.TargetName == virtualMachineTargetVirtualNetwork.TargetName)
                                    {
                                        targetVNetExists = true;
                                        break;
                                    }
                                }

                                if (!targetVNetExists)
                                    this.AddAlert(AlertType.Error, "Target Virtual Network '" + virtualMachineTargetVirtualNetwork.ToString() + "' for Virtual Machine '" + virtualMachine.ToString() + "' Network Interface '" + networkInterface.ToString() + "' is invalid, as it is not included in the migration / template.", networkInterface);
                            }
                        }

                        if (ipConfiguration.TargetSubnet == null)
                            this.AddAlert(AlertType.Error, "Target Subnet for Virtual Machine '" + virtualMachine.ToString() + "' Network Interface '" + networkInterface.ToString() + "' must be specified.", networkInterface);
                    }
                }

                if (virtualMachine.OSVirtualHardDisk.TargetStorageAccount == null)
                    this.AddAlert(AlertType.Error, "Target Storage Account for Virtual Machine '" + virtualMachine.ToString() + "' OS Disk must be specified.", virtualMachine);
                else
                {
                    if (virtualMachine.OSVirtualHardDisk.TargetStorageAccount.GetType() == typeof(Azure.MigrationTarget.StorageAccount))
                    {
                        Azure.MigrationTarget.StorageAccount targetStorageAccount = (Azure.MigrationTarget.StorageAccount)virtualMachine.OSVirtualHardDisk.TargetStorageAccount;
                        bool targetAsmStorageExists = false;

                        foreach (Azure.MigrationTarget.StorageAccount asmStorageAccount in _ExportArtifacts.StorageAccounts)
                        {
                            if (asmStorageAccount.ToString() == targetStorageAccount.ToString())
                            {
                                targetAsmStorageExists = true;
                                break;
                            }
                        }

                        if (!targetAsmStorageExists)
                            this.AddAlert(AlertType.Error, "Target Storage Account '" + targetStorageAccount.ToString() + "' for Virtual Machine '" + virtualMachine.ToString() + "' OS Disk is invalid, as it is not included in the migration / template.", virtualMachine);
                    }
                }

                foreach (MigrationTarget.Disk dataDisk in virtualMachine.DataDisks)
                {
                    if (dataDisk.TargetStorageAccount == null)
                    {
                        this.AddAlert(AlertType.Error, "Target Storage Account for Virtual Machine '" + virtualMachine.ToString() + "' Data Disk '" + dataDisk.ToString() + "' must be specified.", dataDisk);
                    }
                    else
                    {
                        if (dataDisk.TargetStorageAccount.GetType() == typeof(Azure.MigrationTarget.StorageAccount))
                        {
                            Azure.MigrationTarget.StorageAccount targetStorageAccount = (Azure.MigrationTarget.StorageAccount)dataDisk.TargetStorageAccount;
                            bool targetStorageExists = false;

                            foreach (Azure.MigrationTarget.StorageAccount storageAccount in _ExportArtifacts.StorageAccounts)
                            {
                                if (storageAccount.ToString() == targetStorageAccount.ToString())
                                {
                                    targetStorageExists = true;
                                    break;
                                }
                            }

                            if (!targetStorageExists)
                                this.AddAlert(AlertType.Error, "Target Storage Account '" + targetStorageAccount.ToString() + "' for Virtual Machine '" + virtualMachine.ToString() + "' Data Disk '" + dataDisk.ToString() + "' is invalid, as it is not included in the migration / template.", dataDisk);
                        }
                    }
                }
            }

            // todo now asap - Add test for NSGs being present in Migration
            //MigrationTarget.NetworkSecurityGroup targetNetworkSecurityGroup = (MigrationTarget.NetworkSecurityGroup)_ExportArtifacts.SeekNetworkSecurityGroup(targetSubnet.NetworkSecurityGroup.ToString());
            //if (targetNetworkSecurityGroup == null)
            //{
            //    this.AddAlert(AlertType.Error, "Subnet '" + subnet.name + "' utilized ASM Network Security Group (NSG) '" + targetSubnet.NetworkSecurityGroup.ToString() + "', which has not been added to the ARM Subnet as the NSG was not included in the ARM Template (was not selected as an included resources for export).", targetNetworkSecurityGroup);
            //}

            // todo add Warning about availability set with only single VM included

            // todo add error if existing target disk storage is not in the same data center / region as vm.

            LogProvider.WriteLog("UpdateArtifacts", "Start OnTemplateChanged Event");
            OnTemplateChanged();
            LogProvider.WriteLog("UpdateArtifacts", "End OnTemplateChanged Event");

            StatusProvider.UpdateStatus("Ready");

            LogProvider.WriteLog("UpdateArtifacts", "End - Execution " + this.ExecutionGuid.ToString());
        }

        public override async Task GenerateStreams()
        {
            TemplateStreams.Clear();
            Resources.Clear();
            _CopyBlobDetails.Clear();

            LogProvider.WriteLog("GenerateStreams", "Start - Execution " + this.ExecutionGuid.ToString());

            if (_ExportArtifacts != null)
            {
                LogProvider.WriteLog("GenerateStreams", "Start processing selected Network Security Groups");
                foreach (MigrationTarget.NetworkSecurityGroup targetNetworkSecurityGroup in _ExportArtifacts.NetworkSecurityGroups)
                {
                    StatusProvider.UpdateStatus("BUSY: Exporting Network Security Group : " + targetNetworkSecurityGroup.ToString());
                    await BuildNetworkSecurityGroup(targetNetworkSecurityGroup);
                }
                LogProvider.WriteLog("GenerateStreams", "End processing selected Network Security Groups");

                LogProvider.WriteLog("GenerateStreams", "Start processing selected Virtual Networks");
                foreach (Azure.MigrationTarget.VirtualNetwork virtualNetwork in _ExportArtifacts.VirtualNetworks)
                {
                    StatusProvider.UpdateStatus("BUSY: Exporting Virtual Network : " + virtualNetwork.ToString());
                    await BuildVirtualNetworkObject(virtualNetwork);
                }
                LogProvider.WriteLog("GenerateStreams", "End processing selected Virtual Networks");

                LogProvider.WriteLog("GenerateStreams", "Start processing selected Load Balancers");
                foreach (Azure.MigrationTarget.LoadBalancer loadBalancer in _ExportArtifacts.LoadBalancers)
                {
                    StatusProvider.UpdateStatus("BUSY: Exporting Load Balancer : " + loadBalancer.ToString());
                    await BuildLoadBalancerObject(loadBalancer);
                }
                LogProvider.WriteLog("GenerateStreams", "End processing selected Load Balancers");

                LogProvider.WriteLog("GenerateStreams", "Start processing selected Storage Accounts");
                foreach (MigrationTarget.StorageAccount storageAccount in _ExportArtifacts.StorageAccounts)
                {
                    StatusProvider.UpdateStatus("BUSY: Exporting Storage Account : " + storageAccount.ToString());
                    BuildStorageAccountObject(storageAccount);
                }
                LogProvider.WriteLog("GenerateStreams", "End processing selected Storage Accounts");

                LogProvider.WriteLog("GenerateStreams", "Start processing selected Cloud Services / Virtual Machines");
                foreach (Azure.MigrationTarget.VirtualMachine virtualMachine in _ExportArtifacts.VirtualMachines)
                {
                    StatusProvider.UpdateStatus("BUSY: Exporting Virtual Machine : " + virtualMachine.ToString());
                    await BuildVirtualMachineObject(virtualMachine);
                }
                LogProvider.WriteLog("GenerateStreams", "End processing selected Cloud Services / Virtual Machines");
            }
            else
                LogProvider.WriteLog("GenerateStreams", "ExportArtifacts is null, nothing to export.");

            StatusProvider.UpdateStatus("Ready");
            LogProvider.WriteLog("GenerateStreams", "End - Execution " + this.ExecutionGuid.ToString());
        }

        public override async Task SerializeStreams()
        {
            LogProvider.WriteLog("SerializeStreams", "Start Template Stream Update");

            TemplateStreams.Clear();

            await UpdateExportJsonStream();

            ASCIIEncoding asciiEncoding = new ASCIIEncoding();

            // Only generate copyblobdetails.json if it contains disks that are being copied
            if (_CopyBlobDetails.Count > 0)
            {
                StatusProvider.UpdateStatus("BUSY:  Generating copyblobdetails.json");
                LogProvider.WriteLog("SerializeStreams", "Start copyblobdetails.json stream");

                string jsontext = JsonConvert.SerializeObject(this._CopyBlobDetails, Newtonsoft.Json.Formatting.Indented, new JsonSerializerSettings { NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore });
                byte[] b = asciiEncoding.GetBytes(jsontext);
                MemoryStream copyBlobDetailStream = new MemoryStream();
                copyBlobDetailStream.Write(b, 0, b.Length);
                TemplateStreams.Add("copyblobdetails.json", copyBlobDetailStream);

                LogProvider.WriteLog("SerializeStreams", "End copyblobdetails.json stream");
            }

            StatusProvider.UpdateStatus("BUSY:  Generating DeployInstructions.html");
            LogProvider.WriteLog("SerializeStreams", "Start DeployInstructions.html stream");

            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "MigAz.Azure.Generator.DeployDocTemplate.html";
            string instructionContent;

            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            using (StreamReader reader = new StreamReader(stream))
            {
                instructionContent = reader.ReadToEnd();
            }

            string targetResourceGroupName = String.Empty;
            string resourceGroupLocation = String.Empty;
            string azureEnvironmentSwitch = String.Empty;
            string tenantSwitch = String.Empty;
            string subscriptionSwitch = String.Empty;

            if (_ExportArtifacts != null && _ExportArtifacts.ResourceGroup != null)
            {
                targetResourceGroupName = _ExportArtifacts.ResourceGroup.ToString();

                if (_ExportArtifacts.ResourceGroup.TargetLocation != null)
                    resourceGroupLocation = _ExportArtifacts.ResourceGroup.TargetLocation.Name;
            }

            if (this.TargetSubscription != null)
            {

                subscriptionSwitch = " -SubscriptionId '" + this.TargetSubscription.SubscriptionId + "'";


                if (this.TargetSubscription.AzureEnvironment != AzureEnvironment.AzureCloud)
                    azureEnvironmentSwitch = " -EnvironmentName " + this.TargetSubscription.AzureEnvironment.ToString();
                if (this.TargetSubscription.AzureEnvironment != AzureEnvironment.AzureCloud)
                    azureEnvironmentSwitch = " -EnvironmentName " + this.TargetSubscription.AzureEnvironment.ToString();

                if (this.TargetSubscription.AzureAdTenantId != Guid.Empty)
                    tenantSwitch = " -TenantId '" + this.TargetSubscription.AzureAdTenantId.ToString() + "'";
            }


            instructionContent = instructionContent.Replace("{migAzAzureEnvironmentSwitch}", azureEnvironmentSwitch);
            instructionContent = instructionContent.Replace("{tenantSwitch}", tenantSwitch);
            instructionContent = instructionContent.Replace("{subscriptionSwitch}", subscriptionSwitch);
            instructionContent = instructionContent.Replace("{templatePath}", GetTemplatePath());
            instructionContent = instructionContent.Replace("{blobDetailsPath}", GetCopyBlobDetailPath());
            instructionContent = instructionContent.Replace("{resourceGroupName}", targetResourceGroupName);
            instructionContent = instructionContent.Replace("{location}", resourceGroupLocation);
            instructionContent = instructionContent.Replace("{migAzPath}", AppDomain.CurrentDomain.BaseDirectory);
            instructionContent = instructionContent.Replace("{migAzMessages}", BuildMigAzMessages());

            byte[] c = asciiEncoding.GetBytes(instructionContent);
            MemoryStream instructionStream = new MemoryStream();
            instructionStream.Write(c, 0, c.Length);
            TemplateStreams.Add("DeployInstructions.html", instructionStream);

            LogProvider.WriteLog("SerializeStreams", "End DeployInstructions.html stream");

            LogProvider.WriteLog("SerializeStreams", "End Template Stream Update");
            StatusProvider.UpdateStatus("Ready");
        }

        private async Task UpdateExportJsonStream()
        {
            StatusProvider.UpdateStatus("BUSY:  Generating export.json");
            LogProvider.WriteLog("UpdateArtifacts", "Start export.json stream");

            String templateString = await GetTemplateString();
            ASCIIEncoding asciiEncoding = new ASCIIEncoding();
            byte[] a = asciiEncoding.GetBytes(templateString);
            MemoryStream templateStream = new MemoryStream();
            templateStream.Write(a, 0, a.Length);
            TemplateStreams.Add("export.json", templateStream);

            LogProvider.WriteLog("UpdateArtifacts", "End export.json stream");
        }

        protected override void OnTemplateChanged()
        {
            // Call the base class event invocation method.
            base.OnTemplateChanged();
        }



        private AvailabilitySet BuildAvailabilitySetObject(Azure.MigrationTarget.AvailabilitySet availabilitySet)
        {
            LogProvider.WriteLog("BuildAvailabilitySetObject", "Start");

            AvailabilitySet availabilityset = new AvailabilitySet(this.ExecutionGuid);

            availabilityset.name = availabilitySet.ToString();
            availabilityset.location = "[resourceGroup().location]";

            this.AddResource(availabilityset);

            LogProvider.WriteLog("BuildAvailabilitySetObject", "End");

            return availabilityset;
        }

        private async Task BuildPublicIPAddressObject(Azure.MigrationTarget.PublicIp publicIp)
        {
            LogProvider.WriteLog("BuildPublicIPAddressObject", "Start " + ArmConst.ProviderLoadBalancers + publicIp.ToString());

            PublicIPAddress publicipaddress = new PublicIPAddress(this.ExecutionGuid);
            publicipaddress.name = publicIp.ToString();
            publicipaddress.location = "[resourceGroup().location]";

            PublicIPAddress_Properties publicipaddress_properties = new PublicIPAddress_Properties();
            publicipaddress.properties = publicipaddress_properties;

            if (publicIp.DomainNameLabel != String.Empty)
            {
                Hashtable dnssettings = new Hashtable();
                dnssettings.Add("domainNameLabel", publicIp.DomainNameLabel);
                publicipaddress_properties.dnsSettings = dnssettings;
            }

            this.AddResource(publicipaddress);

            LogProvider.WriteLog("BuildPublicIPAddressObject", "End " + ArmConst.ProviderLoadBalancers + publicIp.ToString());
        }

        private async Task BuildLoadBalancerObject(Azure.MigrationTarget.LoadBalancer loadBalancer)
        {
            LogProvider.WriteLog("BuildLoadBalancerObject", "Start " + ArmConst.ProviderLoadBalancers + loadBalancer.ToString());

            List<string> dependson = new List<string>();
            LoadBalancer loadbalancer = new LoadBalancer(this.ExecutionGuid);
            loadbalancer.name = loadBalancer.ToString();
            loadbalancer.location = "[resourceGroup().location]";
            loadbalancer.dependsOn = dependson;

            LoadBalancer_Properties loadbalancer_properties = new LoadBalancer_Properties();
            loadbalancer.properties = loadbalancer_properties;


            List<FrontendIPConfiguration> frontendipconfigurations = new List<FrontendIPConfiguration>();
            loadbalancer_properties.frontendIPConfigurations = frontendipconfigurations;

            foreach (Azure.MigrationTarget.FrontEndIpConfiguration targetFrontEndIpConfiguration in loadBalancer.FrontEndIpConfigurations)
            {
                FrontendIPConfiguration frontendipconfiguration = new FrontendIPConfiguration();
                frontendipconfiguration.name = targetFrontEndIpConfiguration.Name;
                frontendipconfigurations.Add(frontendipconfiguration);

                FrontendIPConfiguration_Properties frontendipconfiguration_properties = new FrontendIPConfiguration_Properties();
                frontendipconfiguration.properties = frontendipconfiguration_properties;

                if (targetFrontEndIpConfiguration.PublicIp == null)
                {
                    frontendipconfiguration_properties.privateIPAllocationMethod = targetFrontEndIpConfiguration.PrivateIPAllocationMethod;
                    frontendipconfiguration_properties.privateIPAddress = targetFrontEndIpConfiguration.PrivateIPAddress;

                    if (targetFrontEndIpConfiguration.TargetVirtualNetwork != null && targetFrontEndIpConfiguration.TargetVirtualNetwork.GetType() == typeof(Azure.MigrationTarget.VirtualNetwork))
                        dependson.Add("[concat(" + ArmConst.ResourceGroupId + ", '" + ArmConst.ProviderVirtualNetwork + targetFrontEndIpConfiguration.TargetVirtualNetwork.ToString() + "')]");

                    Reference subnet_ref = new Reference();
                    frontendipconfiguration_properties.subnet = subnet_ref;

                    if (targetFrontEndIpConfiguration.TargetVirtualNetwork != null && targetFrontEndIpConfiguration.TargetSubnet != null)
                    {
                        subnet_ref.id = targetFrontEndIpConfiguration.TargetSubnet.TargetId;
                    }
                }
                else
                {
                    await BuildPublicIPAddressObject(targetFrontEndIpConfiguration.PublicIp);

                    Reference publicipaddress_ref = new Reference();
                    publicipaddress_ref.id = "[concat(" + ArmConst.ResourceGroupId + ", '" + ArmConst.ProviderPublicIpAddress + targetFrontEndIpConfiguration.PublicIp.ToString() + "')]";
                    frontendipconfiguration_properties.publicIPAddress = publicipaddress_ref;

                    dependson.Add("[concat(" + ArmConst.ResourceGroupId + ", '" + ArmConst.ProviderPublicIpAddress + targetFrontEndIpConfiguration.PublicIp.ToString() + "')]");
                }
            }

            List<Hashtable> backendaddresspools = new List<Hashtable>();
            loadbalancer_properties.backendAddressPools = backendaddresspools;

            foreach (Azure.MigrationTarget.BackEndAddressPool targetBackEndAddressPool in loadBalancer.BackEndAddressPools)
            {
                Hashtable backendaddresspool = new Hashtable();
                backendaddresspool.Add("name", targetBackEndAddressPool.Name);
                backendaddresspools.Add(backendaddresspool);
            }

            List<InboundNatRule> inboundnatrules = new List<InboundNatRule>();
            List<LoadBalancingRule> loadbalancingrules = new List<LoadBalancingRule>();
            List<Probe> probes = new List<Probe>();

            loadbalancer_properties.inboundNatRules = inboundnatrules;
            loadbalancer_properties.loadBalancingRules = loadbalancingrules;
            loadbalancer_properties.probes = probes;

            // Add Inbound Nat Rules
            foreach (Azure.MigrationTarget.InboundNatRule inboundNatRule in loadBalancer.InboundNatRules)
            {
                InboundNatRule_Properties inboundnatrule_properties = new InboundNatRule_Properties();
                inboundnatrule_properties.frontendPort = inboundNatRule.FrontEndPort;
                inboundnatrule_properties.backendPort = inboundNatRule.BackEndPort;
                inboundnatrule_properties.protocol = inboundNatRule.Protocol;

                if (inboundNatRule.FrontEndIpConfiguration != null)
                {
                    Reference frontendIPConfiguration = new Reference();
                    frontendIPConfiguration.id = "[concat(" + ArmConst.ResourceGroupId + ", '" + ArmConst.ProviderLoadBalancers + loadbalancer.name + "/frontendIPConfigurations/default')]";
                    inboundnatrule_properties.frontendIPConfiguration = frontendIPConfiguration;
                }

                InboundNatRule inboundnatrule = new InboundNatRule();
                inboundnatrule.name = inboundNatRule.Name;
                inboundnatrule.properties = inboundnatrule_properties;

                loadbalancer_properties.inboundNatRules.Add(inboundnatrule);
            }

            foreach (Azure.MigrationTarget.Probe targetProbe in loadBalancer.Probes)
            {
                Probe_Properties probe_properties = new Probe_Properties();
                probe_properties.port = targetProbe.Port;
                probe_properties.protocol = targetProbe.Protocol;
                probe_properties.intervalInSeconds = targetProbe.IntervalInSeconds;
                probe_properties.numberOfProbes = targetProbe.NumberOfProbes;
                probe_properties.requestPath = targetProbe.RequestPath;

                Probe probe = new Probe();
                probe.name = targetProbe.Name;
                probe.properties = probe_properties;

                loadbalancer_properties.probes.Add(probe);
            }

            foreach (Azure.MigrationTarget.LoadBalancingRule targetLoadBalancingRule in loadBalancer.LoadBalancingRules)
            {
                Reference frontendipconfiguration_ref = new Reference();
                frontendipconfiguration_ref.id = "[concat(" + ArmConst.ResourceGroupId + ", '" + ArmConst.ProviderLoadBalancers + loadbalancer.name + "/frontendIPConfigurations/" + targetLoadBalancingRule.FrontEndIpConfiguration.Name + "')]";

                Reference backendaddresspool_ref = new Reference();
                backendaddresspool_ref.id = "[concat(" + ArmConst.ResourceGroupId + ", '" + ArmConst.ProviderLoadBalancers + loadbalancer.name + "/backendAddressPools/" + targetLoadBalancingRule.BackEndAddressPool.Name + "')]";

                Reference probe_ref = new Reference();
                probe_ref.id = "[concat(" + ArmConst.ResourceGroupId + ",'" + ArmConst.ProviderLoadBalancers + loadbalancer.name + "/probes/" + targetLoadBalancingRule.Probe.Name + "')]";

                LoadBalancingRule_Properties loadbalancingrule_properties = new LoadBalancingRule_Properties();
                loadbalancingrule_properties.frontendIPConfiguration = frontendipconfiguration_ref;
                loadbalancingrule_properties.backendAddressPool = backendaddresspool_ref;
                loadbalancingrule_properties.probe = probe_ref;
                loadbalancingrule_properties.frontendPort = targetLoadBalancingRule.FrontEndPort;
                loadbalancingrule_properties.backendPort = targetLoadBalancingRule.BackEndPort;
                loadbalancingrule_properties.protocol = targetLoadBalancingRule.Protocol;

                LoadBalancingRule loadbalancingrule = new LoadBalancingRule();
                loadbalancingrule.name = targetLoadBalancingRule.Name;
                loadbalancingrule.properties = loadbalancingrule_properties;

                loadbalancer_properties.loadBalancingRules.Add(loadbalancingrule);
            }

            this.AddResource(loadbalancer);

            LogProvider.WriteLog("BuildLoadBalancerObject", "End " + ArmConst.ProviderLoadBalancers + loadBalancer.ToString());
        }

        private async Task BuildVirtualNetworkObject(Azure.MigrationTarget.VirtualNetwork targetVirtualNetwork)
        {
            LogProvider.WriteLog("BuildVirtualNetworkObject", "Start Microsoft.Network/virtualNetworks/" + targetVirtualNetwork.ToString());

            List<string> dependson = new List<string>();

            AddressSpace addressspace = new AddressSpace();
            addressspace.addressPrefixes = targetVirtualNetwork.AddressPrefixes;

            VirtualNetwork_dhcpOptions dhcpoptions = new VirtualNetwork_dhcpOptions();
            dhcpoptions.dnsServers = targetVirtualNetwork.DnsServers;

            VirtualNetwork virtualnetwork = new VirtualNetwork(this.ExecutionGuid);
            virtualnetwork.name = targetVirtualNetwork.ToString();
            virtualnetwork.location = "[resourceGroup().location]";
            virtualnetwork.dependsOn = dependson;

            List<Subnet> subnets = new List<Subnet>();
            foreach (Azure.MigrationTarget.Subnet targetSubnet in targetVirtualNetwork.TargetSubnets)
            {
                Subnet_Properties properties = new Subnet_Properties();
                properties.addressPrefix = targetSubnet.AddressPrefix;

                Subnet subnet = new Subnet();
                subnet.name = targetSubnet.TargetName;
                subnet.properties = properties;

                subnets.Add(subnet);

                // add Network Security Group if exists
                if (targetSubnet.NetworkSecurityGroup != null)
                {
                    Core.ArmTemplate.NetworkSecurityGroup networksecuritygroup = await BuildNetworkSecurityGroup(targetSubnet.NetworkSecurityGroup);
                    // Add NSG reference to the subnet
                    Reference networksecuritygroup_ref = new Reference();
                    networksecuritygroup_ref.id = "[concat(" + ArmConst.ResourceGroupId + ", '" + ArmConst.ProviderNetworkSecurityGroups + networksecuritygroup.name + "')]";

                    properties.networkSecurityGroup = networksecuritygroup_ref;

                    // Add NSG dependsOn to the Virtual Network object
                    if (!virtualnetwork.dependsOn.Contains(networksecuritygroup_ref.id))
                    {
                        virtualnetwork.dependsOn.Add(networksecuritygroup_ref.id);
                    }
                }

                // add Route Table if exists
                if (targetSubnet.RouteTable != null)
                {
                    RouteTable routetable = await BuildRouteTable(targetSubnet.RouteTable);

                    // Add Route Table reference to the subnet
                    Reference routetable_ref = new Reference();
                    routetable_ref.id = "[concat(" + ArmConst.ResourceGroupId + ", '" + ArmConst.ProviderRouteTables + routetable.name + "')]";

                    properties.routeTable = routetable_ref;

                    // Add Route Table dependsOn to the Virtual Network object
                    if (!virtualnetwork.dependsOn.Contains(routetable_ref.id))
                    {
                        virtualnetwork.dependsOn.Add(routetable_ref.id);
                    }
                }
            }

            VirtualNetwork_Properties virtualnetwork_properties = new VirtualNetwork_Properties();
            virtualnetwork_properties.addressSpace = addressspace;
            virtualnetwork_properties.subnets = subnets;
            virtualnetwork_properties.dhcpOptions = dhcpoptions;

            virtualnetwork.properties = virtualnetwork_properties;

            this.AddResource(virtualnetwork);

            await AddGatewaysToVirtualNetwork(targetVirtualNetwork, virtualnetwork);
            
            LogProvider.WriteLog("BuildVirtualNetworkObject", "End Microsoft.Network/virtualNetworks/" + targetVirtualNetwork.ToString());
        }

        private async Task AddGatewaysToVirtualNetwork(MigrationTarget.VirtualNetwork targetVirtualNetwork, VirtualNetwork templateVirtualNetwork)
        {
            if (targetVirtualNetwork.SourceVirtualNetwork.GetType() == typeof(Azure.Asm.VirtualNetwork))
            {
                Asm.VirtualNetwork asmVirtualNetwork = (Asm.VirtualNetwork)targetVirtualNetwork.SourceVirtualNetwork;

                // Process Virtual Network Gateway, if exists
                if ((asmVirtualNetwork.Gateway != null) && (asmVirtualNetwork.Gateway.IsProvisioned))
                {
                    // Gateway Public IP Address
                    PublicIPAddress_Properties publicipaddress_properties = new PublicIPAddress_Properties();
                    publicipaddress_properties.publicIPAllocationMethod = "Dynamic";

                    PublicIPAddress publicipaddress = new PublicIPAddress(this.ExecutionGuid);
                    publicipaddress.name = targetVirtualNetwork.TargetName + _settingsProvider.VirtualNetworkGatewaySuffix + _settingsProvider.PublicIPSuffix;
                    publicipaddress.location = "[resourceGroup().location]";
                    publicipaddress.properties = publicipaddress_properties;

                    this.AddResource(publicipaddress);

                    // Virtual Network Gateway
                    Reference subnet_ref = new Reference();
                    subnet_ref.id = "[concat(" + ArmConst.ResourceGroupId + ", '" + ArmConst.ProviderVirtualNetwork + templateVirtualNetwork.name + "/subnets/" + ArmConst.GatewaySubnetName + "')]";

                    Reference publicipaddress_ref = new Reference();
                    publicipaddress_ref.id = "[concat(" + ArmConst.ResourceGroupId + ", '" + ArmConst.ProviderPublicIpAddress + publicipaddress.name + "')]";

                    var dependson = new List<string>();
                    dependson.Add("[concat(" + ArmConst.ResourceGroupId + ", '" + ArmConst.ProviderVirtualNetwork + templateVirtualNetwork.name + "')]");
                    dependson.Add("[concat(" + ArmConst.ResourceGroupId + ", '" + ArmConst.ProviderPublicIpAddress + publicipaddress.name + "')]");

                    IpConfiguration_Properties ipconfiguration_properties = new IpConfiguration_Properties();
                    ipconfiguration_properties.privateIPAllocationMethod = "Dynamic";
                    ipconfiguration_properties.subnet = subnet_ref;
                    ipconfiguration_properties.publicIPAddress = publicipaddress_ref;

                    IpConfiguration virtualnetworkgateway_ipconfiguration = new IpConfiguration();
                    virtualnetworkgateway_ipconfiguration.name = "GatewayIPConfig";
                    virtualnetworkgateway_ipconfiguration.properties = ipconfiguration_properties;

                    VirtualNetworkGateway_Sku virtualnetworkgateway_sku = new VirtualNetworkGateway_Sku();
                    virtualnetworkgateway_sku.name = "Basic";
                    virtualnetworkgateway_sku.tier = "Basic";

                    List<IpConfiguration> virtualnetworkgateway_ipconfigurations = new List<IpConfiguration>();
                    virtualnetworkgateway_ipconfigurations.Add(virtualnetworkgateway_ipconfiguration);

                    VirtualNetworkGateway_Properties virtualnetworkgateway_properties = new VirtualNetworkGateway_Properties();
                    virtualnetworkgateway_properties.ipConfigurations = virtualnetworkgateway_ipconfigurations;
                    virtualnetworkgateway_properties.sku = virtualnetworkgateway_sku;

                    // If there is VPN Client configuration
                    if (asmVirtualNetwork.VPNClientAddressPrefixes.Count > 0)
                    {
                        AddressSpace vpnclientaddresspool = new AddressSpace();
                        vpnclientaddresspool.addressPrefixes = asmVirtualNetwork.VPNClientAddressPrefixes;

                        VPNClientConfiguration vpnclientconfiguration = new VPNClientConfiguration();
                        vpnclientconfiguration.vpnClientAddressPool = vpnclientaddresspool;

                        //Process vpnClientRootCertificates
                        List<VPNClientCertificate> vpnclientrootcertificates = new List<VPNClientCertificate>();
                        foreach (Asm.ClientRootCertificate certificate in asmVirtualNetwork.ClientRootCertificates)
                        {
                            VPNClientCertificate_Properties vpnclientcertificate_properties = new VPNClientCertificate_Properties();
                            vpnclientcertificate_properties.PublicCertData = certificate.PublicCertData;

                            VPNClientCertificate vpnclientcertificate = new VPNClientCertificate();
                            vpnclientcertificate.name = certificate.TargetSubject;
                            vpnclientcertificate.properties = vpnclientcertificate_properties;

                            vpnclientrootcertificates.Add(vpnclientcertificate);
                        }

                        vpnclientconfiguration.vpnClientRootCertificates = vpnclientrootcertificates;

                        virtualnetworkgateway_properties.vpnClientConfiguration = vpnclientconfiguration;
                    }

                    if (asmVirtualNetwork.LocalNetworkSites.Count > 0 && asmVirtualNetwork.LocalNetworkSites[0].ConnectionType == "Dedicated")
                    {
                        virtualnetworkgateway_properties.gatewayType = "ExpressRoute";
                        virtualnetworkgateway_properties.enableBgp = null;
                        virtualnetworkgateway_properties.vpnType = null;
                    }
                    else
                    {
                        virtualnetworkgateway_properties.gatewayType = "Vpn";
                        string vpnType = asmVirtualNetwork.Gateway.GatewayType;
                        if (vpnType == "StaticRouting")
                        {
                            vpnType = "PolicyBased";
                        }
                        else if (vpnType == "DynamicRouting")
                        {
                            vpnType = "RouteBased";
                        }
                        virtualnetworkgateway_properties.vpnType = vpnType;
                    }

                    VirtualNetworkGateway virtualnetworkgateway = new VirtualNetworkGateway(this.ExecutionGuid);
                    virtualnetworkgateway.location = "[resourceGroup().location]";
                    virtualnetworkgateway.name = targetVirtualNetwork.TargetName + _settingsProvider.VirtualNetworkGatewaySuffix;
                    virtualnetworkgateway.properties = virtualnetworkgateway_properties;
                    virtualnetworkgateway.dependsOn = dependson;

                    this.AddResource(virtualnetworkgateway);

                    if (!asmVirtualNetwork.HasGatewaySubnet)
                        this.AddAlert(AlertType.Error, "The Virtual Network '" + targetVirtualNetwork.TargetName + "' does not contain the necessary '" + ArmConst.GatewaySubnetName + "' subnet for deployment of the '" + virtualnetworkgateway.name + "' Gateway.", asmVirtualNetwork);

                    await AddLocalSiteToGateway(asmVirtualNetwork, templateVirtualNetwork, virtualnetworkgateway);
                }
            }
        }

        private async Task AddLocalSiteToGateway(Asm.VirtualNetwork asmVirtualNetwork, VirtualNetwork virtualnetwork, VirtualNetworkGateway virtualnetworkgateway)
        {
            // Local Network Gateways & Connections
            foreach (Asm.LocalNetworkSite asmLocalNetworkSite in asmVirtualNetwork.LocalNetworkSites)
            {
                GatewayConnection_Properties gatewayconnection_properties = new GatewayConnection_Properties();
                var dependson = new List<string>();

                if (asmLocalNetworkSite.ConnectionType == "IPsec")
                {
                    // Local Network Gateway
                    List<String> addressprefixes = asmLocalNetworkSite.AddressPrefixes;

                    AddressSpace localnetworkaddressspace = new AddressSpace();
                    localnetworkaddressspace.addressPrefixes = addressprefixes;

                    LocalNetworkGateway_Properties localnetworkgateway_properties = new LocalNetworkGateway_Properties();
                    localnetworkgateway_properties.localNetworkAddressSpace = localnetworkaddressspace;
                    localnetworkgateway_properties.gatewayIpAddress = asmLocalNetworkSite.VpnGatewayAddress;

                    LocalNetworkGateway localnetworkgateway = new LocalNetworkGateway(this.ExecutionGuid);
                    localnetworkgateway.name = asmLocalNetworkSite.Name + "-LocalGateway";
                    localnetworkgateway.name = localnetworkgateway.name.Replace(" ", String.Empty);

                    localnetworkgateway.location = "[resourceGroup().location]";
                    localnetworkgateway.properties = localnetworkgateway_properties;

                    this.AddResource(localnetworkgateway);

                    Reference localnetworkgateway_ref = new Reference();
                    localnetworkgateway_ref.id = "[concat(" + ArmConst.ResourceGroupId + ", '" + ArmConst.ProviderLocalNetworkGateways + localnetworkgateway.name + "')]";
                    dependson.Add(localnetworkgateway_ref.id);

                    gatewayconnection_properties.connectionType = asmLocalNetworkSite.ConnectionType;
                    gatewayconnection_properties.localNetworkGateway2 = localnetworkgateway_ref;

                    string connectionShareKey = asmLocalNetworkSite.SharedKey;
                    if (connectionShareKey == String.Empty)
                    {
                        gatewayconnection_properties.sharedKey = "***SHARED KEY GOES HERE***";
                        this.AddAlert(AlertType.Error, $"Unable to retrieve shared key for VPN connection '{virtualnetworkgateway.name}'. Please edit the template to provide this value.", asmVirtualNetwork);
                    }
                    else
                    {
                        gatewayconnection_properties.sharedKey = connectionShareKey;
                    }
                }
                else if (asmLocalNetworkSite.ConnectionType == "Dedicated")
                {
                    gatewayconnection_properties.connectionType = "ExpressRoute";
                    gatewayconnection_properties.peer = new Reference() { id = "/subscriptions/***/resourceGroups/***" + ArmConst.ProviderExpressRouteCircuits + "***" }; // todo, this is incomplete
                    this.AddAlert(AlertType.Error, $"Gateway '{virtualnetworkgateway.name}' connects to ExpressRoute. MigAz is unable to migrate ExpressRoute circuits. Please create or convert the circuit yourself and update the circuit resource ID in the generated template.", asmVirtualNetwork);
                }

                // Connections
                Reference virtualnetworkgateway_ref = new Reference();
                virtualnetworkgateway_ref.id = "[concat(" + ArmConst.ResourceGroupId + ", '" + ArmConst.ProviderVirtualNetworkGateways + virtualnetworkgateway.name + "')]";
                     
                dependson.Add(virtualnetworkgateway_ref.id);

                gatewayconnection_properties.virtualNetworkGateway1 = virtualnetworkgateway_ref;

                GatewayConnection gatewayconnection = new GatewayConnection(this.ExecutionGuid);
                gatewayconnection.name = virtualnetworkgateway.name + "-" + asmLocalNetworkSite.TargetName + "-connection"; // TODO, HardCoded
                gatewayconnection.location = "[resourceGroup().location]";
                gatewayconnection.properties = gatewayconnection_properties;
                gatewayconnection.dependsOn = dependson;

                this.AddResource(gatewayconnection);

            }
        }

        private async Task<NetworkSecurityGroup> BuildNetworkSecurityGroup(MigrationTarget.NetworkSecurityGroup targetNetworkSecurityGroup)
        {
            LogProvider.WriteLog("BuildNetworkSecurityGroup", "Start");

            NetworkSecurityGroup networksecuritygroup = new NetworkSecurityGroup(this.ExecutionGuid);
            networksecuritygroup.name = targetNetworkSecurityGroup.ToString();
            networksecuritygroup.location = "[resourceGroup().location]";

            NetworkSecurityGroup_Properties networksecuritygroup_properties = new NetworkSecurityGroup_Properties();
            networksecuritygroup_properties.securityRules = new List<SecurityRule>();

            // for each rule
            foreach (MigrationTarget.NetworkSecurityGroupRule targetNetworkSecurityGroupRule in targetNetworkSecurityGroup.Rules)
            {
                // if not system rule
                if (!targetNetworkSecurityGroupRule.IsSystemRule)
                {
                    SecurityRule_Properties securityrule_properties = new SecurityRule_Properties();
                    securityrule_properties.description = targetNetworkSecurityGroupRule.ToString();
                    securityrule_properties.direction = targetNetworkSecurityGroupRule.Direction;
                    securityrule_properties.priority = targetNetworkSecurityGroupRule.Priority;
                    securityrule_properties.access = targetNetworkSecurityGroupRule.Access;
                    securityrule_properties.sourceAddressPrefix = targetNetworkSecurityGroupRule.SourceAddressPrefix;
                    securityrule_properties.destinationAddressPrefix = targetNetworkSecurityGroupRule.DestinationAddressPrefix;
                    securityrule_properties.sourcePortRange = targetNetworkSecurityGroupRule.SourcePortRange;
                    securityrule_properties.destinationPortRange = targetNetworkSecurityGroupRule.DestinationPortRange;
                    securityrule_properties.protocol = targetNetworkSecurityGroupRule.Protocol;

                    SecurityRule securityrule = new SecurityRule();
                    securityrule.name = targetNetworkSecurityGroupRule.ToString();
                    securityrule.properties = securityrule_properties;

                    networksecuritygroup_properties.securityRules.Add(securityrule);
                }
            }

            networksecuritygroup.properties = networksecuritygroup_properties;

            this.AddResource(networksecuritygroup);

            LogProvider.WriteLog("BuildNetworkSecurityGroup", "End");

            return networksecuritygroup;
        }

        private async Task<RouteTable> BuildRouteTable(MigrationTarget.RouteTable routeTable)
        {
            LogProvider.WriteLog("BuildRouteTable", "Start");

            RouteTable routetable = new RouteTable(this.ExecutionGuid);
            routetable.name = routeTable.ToString();
            routetable.location = "[resourceGroup().location]";

            RouteTable_Properties routetable_properties = new RouteTable_Properties();
            routetable_properties.routes = new List<Route>();

            // for each route
            foreach (MigrationTarget.Route migrationRoute in routeTable.Routes)
            {
                //securityrule_properties.protocol = rule.SelectSingleNode("Protocol").InnerText;
                Route_Properties route_properties = new Route_Properties();
                route_properties.addressPrefix = migrationRoute.AddressPrefix;

                // convert next hop type string
                switch (migrationRoute.NextHopType)
                {
                    case "VirtualAppliance":
                        route_properties.nextHopType = "VirtualAppliance";
                        break;
                    case "VPNGateway":
                        route_properties.nextHopType = "VirtualNetworkGateway";
                        break;
                    case "Internet":
                        route_properties.nextHopType = "Internet";
                        break;
                    case "VNETLocal":
                        route_properties.nextHopType = "VnetLocal";
                        break;
                    case "Null":
                        route_properties.nextHopType = "None";
                        break;
                }
                if (route_properties.nextHopType == "VirtualAppliance")
                    route_properties.nextHopIpAddress = migrationRoute.NextHopIpAddress;

                Route route = new Route();
                route.name = migrationRoute.ToString();
                route.properties = route_properties;

                routetable_properties.routes.Add(route);
            }

            routetable.properties = routetable_properties;

            this.AddResource(routetable);

            LogProvider.WriteLog("BuildRouteTable", "End");

            return routetable;
        }

        private Core.ArmTemplate.RouteTable BuildARMRouteTable(Arm.RouteTable routeTable)
        {
            LogProvider.WriteLog("BuildRouteTable", "Start Microsoft.Network/routeTables/" + routeTable.Name);

            Core.ArmTemplate.RouteTable routetable = new Core.ArmTemplate.RouteTable(this.ExecutionGuid);
            routetable.name = routeTable.Name;
            routetable.location = "[resourceGroup().location]";

            RouteTable_Properties routetable_properties = new RouteTable_Properties();
            routetable_properties.routes = new List<Core.ArmTemplate.Route>();

            // for each route
            foreach (Arm.Route armRoute in routeTable.Routes)
            {
                //securityrule_properties.protocol = rule.SelectSingleNode("Protocol").InnerText;
                Route_Properties route_properties = new Route_Properties();
                route_properties.addressPrefix = armRoute.AddressPrefix;
                route_properties.nextHopType = armRoute.NextHopType;


                if (route_properties.nextHopType == "VirtualAppliance")
                    route_properties.nextHopIpAddress = armRoute.NextHopIpAddress;

                Core.ArmTemplate.Route route = new Core.ArmTemplate.Route();
                route.name = armRoute.Name;
                route.properties = route_properties;

                routetable_properties.routes.Add(route);
            }

            routetable.properties = routetable_properties;

            this.AddResource(routetable);

            LogProvider.WriteLog("BuildRouteTable", "End Microsoft.Network/routeTables/" + routeTable.Name);

            return routetable;
        }

        private NetworkInterface BuildNetworkInterfaceObject(Azure.MigrationTarget.NetworkInterface targetNetworkInterface, List<NetworkProfile_NetworkInterface> networkinterfaces)
        {
            LogProvider.WriteLog("BuildNetworkInterfaceObject", "Start " + ArmConst.ProviderNetworkInterfaces + targetNetworkInterface.ToString());

            List<string> dependson = new List<string>();

            NetworkInterface networkInterface = new NetworkInterface(this.ExecutionGuid);
            networkInterface.name = targetNetworkInterface.ToString();
            networkInterface.location = "[resourceGroup().location]";

            List<IpConfiguration> ipConfigurations = new List<IpConfiguration>();
            foreach (Azure.MigrationTarget.NetworkInterfaceIpConfiguration ipConfiguration in targetNetworkInterface.TargetNetworkInterfaceIpConfigurations)
            {
                IpConfiguration ipconfiguration = new IpConfiguration();
                ipconfiguration.name = ipConfiguration.ToString(); 
                IpConfiguration_Properties ipconfiguration_properties = new IpConfiguration_Properties();
                ipconfiguration.properties = ipconfiguration_properties;
                Reference subnet_ref = new Reference();
                ipconfiguration_properties.subnet = subnet_ref;

                if (ipConfiguration.TargetSubnet != null)
                {
                    subnet_ref.id = ipConfiguration.TargetSubnet.TargetId;
                }

                ipconfiguration_properties.privateIPAllocationMethod = ipConfiguration.TargetPrivateIPAllocationMethod;
                ipconfiguration_properties.privateIPAddress = ipConfiguration.TargetPrivateIpAddress;

                if (ipConfiguration.TargetVirtualNetwork != null)
                {
                    if (ipConfiguration.TargetVirtualNetwork.GetType() == typeof(MigrationTarget.VirtualNetwork))
                    {
                        // only adding VNet DependsOn here because as it is a resource in the target migration (resource group)
                        MigrationTarget.VirtualNetwork targetVirtualNetwork = (MigrationTarget.VirtualNetwork)ipConfiguration.TargetVirtualNetwork;
                        dependson.Add(targetVirtualNetwork.TargetId);
                    }
                }

                // If there is at least one endpoint add the reference to the LB backend pool
                List<Reference> loadBalancerBackendAddressPools = new List<Reference>();
                ipconfiguration_properties.loadBalancerBackendAddressPools = loadBalancerBackendAddressPools;

                if (targetNetworkInterface.BackEndAddressPool != null)
                {
                    Reference loadBalancerBackendAddressPool = new Reference();
                    loadBalancerBackendAddressPool.id = "[concat(" + ArmConst.ResourceGroupId + ", '" + ArmConst.ProviderLoadBalancers + targetNetworkInterface.BackEndAddressPool.LoadBalancer.Name + "/backendAddressPools/" + targetNetworkInterface.BackEndAddressPool.Name + "')]";

                    loadBalancerBackendAddressPools.Add(loadBalancerBackendAddressPool);

                    dependson.Add("[concat(" + ArmConst.ResourceGroupId + ", '" + ArmConst.ProviderLoadBalancers + targetNetworkInterface.BackEndAddressPool.LoadBalancer.Name + "')]");
                }

                // Adds the references to the inboud nat rules
                List<Reference> loadBalancerInboundNatRules = new List<Reference>();
                foreach (MigrationTarget.InboundNatRule inboundNatRule in targetNetworkInterface.InboundNatRules)
                {
                    Reference loadBalancerInboundNatRule = new Reference();
                    loadBalancerInboundNatRule.id = "[concat(" + ArmConst.ResourceGroupId + ", '" + ArmConst.ProviderLoadBalancers + inboundNatRule.LoadBalancer.Name + "/inboundNatRules/" + inboundNatRule.Name + "')]";

                    loadBalancerInboundNatRules.Add(loadBalancerInboundNatRule);
                }

                ipconfiguration_properties.loadBalancerInboundNatRules = loadBalancerInboundNatRules;

                if (targetNetworkInterface.HasPublicIPs)
                {
                    PublicIPAddress publicipaddress = new PublicIPAddress(this.ExecutionGuid);
                    publicipaddress.name = targetNetworkInterface.ToString();
                    publicipaddress.location = "[resourceGroup().location]";
                    publicipaddress.properties = new PublicIPAddress_Properties();

                    Core.ArmTemplate.Reference publicIPAddress = new Core.ArmTemplate.Reference();
                    publicIPAddress.id = "[concat(" + ArmConst.ResourceGroupId + ", '" + ArmConst.ProviderPublicIpAddress + publicipaddress.name + "')]";
                    ipconfiguration_properties.publicIPAddress = publicIPAddress;

                    this.AddResource(publicipaddress);
                    dependson.Add(publicIPAddress.id);
                }

                ipConfigurations.Add(ipconfiguration);
            }

            NetworkInterface_Properties networkinterface_properties = new NetworkInterface_Properties();
            networkinterface_properties.ipConfigurations = ipConfigurations;
            networkinterface_properties.enableIPForwarding = targetNetworkInterface.EnableIPForwarding;

            networkInterface.properties = networkinterface_properties;
            networkInterface.dependsOn = dependson;

            NetworkProfile_NetworkInterface_Properties networkinterface_ref_properties = new NetworkProfile_NetworkInterface_Properties();
            networkinterface_ref_properties.primary = targetNetworkInterface.IsPrimary;

            NetworkProfile_NetworkInterface networkinterface_ref = new NetworkProfile_NetworkInterface();
            networkinterface_ref.id = "[concat(" + ArmConst.ResourceGroupId + ", '" + ArmConst.ProviderNetworkInterfaces + networkInterface.name + "')]";
            networkinterface_ref.properties = networkinterface_ref_properties;

            if (targetNetworkInterface.NetworkSecurityGroup != null)
            {
                MigrationTarget.NetworkSecurityGroup networkSecurityGroupInMigration = _ExportArtifacts.SeekNetworkSecurityGroup(targetNetworkInterface.NetworkSecurityGroup.ToString());

                if (networkSecurityGroupInMigration == null)
                {
                    this.AddAlert(AlertType.Error, "Network Interface Card (NIC) '" + networkInterface.name + "' utilized ASM Network Security Group (NSG) '" + targetNetworkInterface.NetworkSecurityGroup.ToString() + "', which has not been added to the NIC as the NSG was not included in the ARM Template (was not selected as an included resources for export).", networkSecurityGroupInMigration);
                }
                else
                {
                    // Add NSG reference to the network interface
                    Reference networksecuritygroup_ref = new Reference();
                    networksecuritygroup_ref.id = "[concat(" + ArmConst.ResourceGroupId + ", '" + ArmConst.ProviderNetworkSecurityGroups + networkSecurityGroupInMigration.ToString() + "')]";

                    networkinterface_properties.NetworkSecurityGroup = networksecuritygroup_ref;
                    networkInterface.properties = networkinterface_properties;

                    // Add NSG dependsOn to the Network Interface object
                    if (!networkInterface.dependsOn.Contains(networksecuritygroup_ref.id))
                    {
                        networkInterface.dependsOn.Add(networksecuritygroup_ref.id);
                    }
                }
            }

            networkinterfaces.Add(networkinterface_ref);

            this.AddResource(networkInterface);

            LogProvider.WriteLog("BuildNetworkInterfaceObject", "End " + ArmConst.ProviderNetworkInterfaces + targetNetworkInterface.ToString());

            return networkInterface;
        }

        private async Task BuildVirtualMachineObject(Azure.MigrationTarget.VirtualMachine virtualMachine)
        {
            LogProvider.WriteLog("BuildVirtualMachineObject", "Start Microsoft.Compute/virtualMachines/" + virtualMachine.ToString());

            VirtualMachine virtualmachine = new VirtualMachine(this.ExecutionGuid);
            virtualmachine.name = virtualMachine.ToString();
            virtualmachine.location = "[resourceGroup().location]";

            List<IStorageTarget> storageaccountdependencies = new List<IStorageTarget>();
            List<string> dependson = new List<string>();

            string newdiskurl = String.Empty;
            string osDiskTargetStorageAccountName = String.Empty;
            if (virtualMachine.OSVirtualHardDisk.TargetStorageAccount != null)
            {
                osDiskTargetStorageAccountName = virtualMachine.OSVirtualHardDisk.TargetStorageAccount.ToString();
                newdiskurl = virtualMachine.OSVirtualHardDisk.TargetMediaLink;
                storageaccountdependencies.Add(virtualMachine.OSVirtualHardDisk.TargetStorageAccount);
            }

            // process network interface
            List<NetworkProfile_NetworkInterface> networkinterfaces = new List<NetworkProfile_NetworkInterface>();

            foreach (MigrationTarget.NetworkInterface targetNetworkInterface in virtualMachine.NetworkInterfaces)
            {
                NetworkInterface networkInterface = BuildNetworkInterfaceObject(targetNetworkInterface, networkinterfaces);
                dependson.Add("[concat(" + ArmConst.ResourceGroupId + ", '" + ArmConst.ProviderNetworkInterfaces + networkInterface.name + "')]");
            }

            HardwareProfile hardwareprofile = new HardwareProfile();
            hardwareprofile.vmSize = GetVMSize(virtualMachine.TargetSize);

            NetworkProfile networkprofile = new NetworkProfile();
            networkprofile.networkInterfaces = networkinterfaces;

            Vhd vhd = new Vhd();
            vhd.uri = newdiskurl;

            OsDisk osdisk = new OsDisk();
            osdisk.name = virtualMachine.OSVirtualHardDisk.ToString();
            osdisk.vhd = vhd;
            osdisk.caching = virtualMachine.OSVirtualHardDisk.HostCaching;

            ImageReference imagereference = new ImageReference();
            OsProfile osprofile = new OsProfile();

            // if the tool is configured to create new VMs with empty data disks
            if (_settingsProvider.BuildEmpty)
            {
                osdisk.createOption = "FromImage";

                osprofile.computerName = virtualMachine.ToString();
                osprofile.adminUsername = "[parameters('adminUsername')]";
                osprofile.adminPassword = "[parameters('adminPassword')]";

                if (!this.Parameters.ContainsKey("adminUsername"))
                {
                    Parameter parameter = new Parameter();
                    parameter.type = "string";
                    this.Parameters.Add("adminUsername", parameter);
                }

                if (!this.Parameters.ContainsKey("adminPassword"))
                {
                    Parameter parameter = new Parameter();
                    parameter.type = "securestring";
                    this.Parameters.Add("adminPassword", parameter);
                }

                if (virtualMachine.OSVirtualHardDiskOS == "Windows")
                {
                    imagereference.publisher = "MicrosoftWindowsServer";
                    imagereference.offer = "WindowsServer";
                    imagereference.sku = "2016-Datacenter";
                    imagereference.version = "latest";
                }
                else if (virtualMachine.OSVirtualHardDiskOS == "Linux")
                {
                    imagereference.publisher = "Canonical";
                    imagereference.offer = "UbuntuServer";
                    imagereference.sku = "16.04.0-LTS";
                    imagereference.version = "latest";
                }
                else
                {
                    imagereference.publisher = "<publisher>";
                    imagereference.offer = "<offer>";
                    imagereference.sku = "<sku>";
                    imagereference.version = "<version>";
                }
            }
            // if the tool is configured to attach copied disks
            else
            {
                osdisk.createOption = "Attach";
                osdisk.osType = virtualMachine.OSVirtualHardDiskOS;

                this._CopyBlobDetails.Add(BuildCopyBlob(virtualMachine.OSVirtualHardDisk));
            }

            // process data disks
            List<DataDisk> datadisks = new List<DataDisk>();
            foreach (MigrationTarget.Disk dataDisk in virtualMachine.DataDisks)
            {
                if (dataDisk.TargetStorageAccount != null)
                {
                    DataDisk datadisk = new DataDisk();
                    datadisk.name = dataDisk.ToString();
                    datadisk.caching = dataDisk.HostCaching;
                    if (dataDisk.DiskSizeInGB != null)
                        datadisk.diskSizeGB = dataDisk.DiskSizeInGB.Value;
                    if (dataDisk.Lun.HasValue)
                        datadisk.lun = dataDisk.Lun.Value;

                    newdiskurl = dataDisk.TargetMediaLink;

                    // if the tool is configured to create new VMs with empty data disks
                    if (_settingsProvider.BuildEmpty)
                    {
                        datadisk.createOption = "Empty";
                    }
                    // if the tool is configured to attach copied disks
                    else
                    {
                        datadisk.createOption = "Attach";

                        // Block of code to help copying the blobs to the new storage accounts
                        this._CopyBlobDetails.Add(BuildCopyBlob(dataDisk));
                        // end of block of code to help copying the blobs to the new storage accounts
                    }

                    vhd = new Vhd();
                    vhd.uri = newdiskurl;
                    datadisk.vhd = vhd;

                    if (!storageaccountdependencies.Contains(dataDisk.TargetStorageAccount))
                        storageaccountdependencies.Add(dataDisk.TargetStorageAccount);

                    datadisks.Add(datadisk);
                }
            }

            StorageProfile storageprofile = new StorageProfile();
            if (_settingsProvider.BuildEmpty) { storageprofile.imageReference = imagereference; }
            storageprofile.osDisk = osdisk;
            storageprofile.dataDisks = datadisks;

            VirtualMachine_Properties virtualmachine_properties = new VirtualMachine_Properties();
            virtualmachine_properties.hardwareProfile = hardwareprofile;
            if (_settingsProvider.BuildEmpty) { virtualmachine_properties.osProfile = osprofile; }
            virtualmachine_properties.networkProfile = networkprofile;
            virtualmachine_properties.storageProfile = storageprofile;

            // process availability set
            if (virtualMachine.TargetAvailabilitySet != null)
            {
                AvailabilitySet availabilitySet = BuildAvailabilitySetObject(virtualMachine.TargetAvailabilitySet);

                // Availability Set
                if (availabilitySet != null)
                {
                    Reference availabilitySetReference = new Reference();
                    virtualmachine_properties.availabilitySet = availabilitySetReference;
                    availabilitySetReference.id = "[concat(" + ArmConst.ResourceGroupId + ", '" + ArmConst.ProviderAvailabilitySets + availabilitySet.name + "')]";
                    dependson.Add("[concat(" + ArmConst.ResourceGroupId + ", '" + ArmConst.ProviderAvailabilitySets + availabilitySet.name + "')]");
                }
            }

            foreach (IStorageTarget storageaccountdependency in storageaccountdependencies)
            {
                if (storageaccountdependency.GetType() == typeof(Azure.MigrationTarget.StorageAccount)) // only add depends on if it is a Storage Account in the target template.  Otherwise, we'll get a "not in template" error for a resource that exists in another Resource Group.
                    dependson.Add("[concat(" + ArmConst.ResourceGroupId + ", '" + ArmConst.ProviderStorageAccounts + storageaccountdependency + "')]");
            }

            virtualmachine.properties = virtualmachine_properties;
            virtualmachine.dependsOn = dependson;
            virtualmachine.resources = new List<ArmResource>();

            // Diagnostics Extension
            Extension extension_iaasdiagnostics = null;
            if (extension_iaasdiagnostics != null) { virtualmachine.resources.Add(extension_iaasdiagnostics); }

            this.AddResource(virtualmachine);

            LogProvider.WriteLog("BuildVirtualMachineObject", "Start Microsoft.Compute/virtualMachines/" + virtualMachine.ToString());
        }

        private CopyBlobDetail BuildCopyBlob(MigrationTarget.Disk disk)
        {
            if (disk.SourceDisk == null)
                return null;

            CopyBlobDetail copyblobdetail = new CopyBlobDetail();
            if (this.SourceSubscription != null)
                copyblobdetail.SourceEnvironment = this.SourceSubscription.AzureEnvironment.ToString();

            if (disk.SourceDisk != null && disk.SourceDisk.GetType() == typeof(Asm.Disk))
            {
                Asm.Disk asmDataDisk = (Asm.Disk)disk.SourceDisk;

                copyblobdetail.SourceSA = asmDataDisk.StorageAccountName;
                copyblobdetail.SourceContainer = asmDataDisk.StorageAccountContainer;
                copyblobdetail.SourceBlob = asmDataDisk.StorageAccountBlob;

                if (asmDataDisk.SourceStorageAccount != null && asmDataDisk.SourceStorageAccount.Keys != null)
                    copyblobdetail.SourceKey = asmDataDisk.SourceStorageAccount.Keys.Primary;
            }
            else if (disk.SourceDisk != null && disk.SourceDisk.GetType() == typeof(Arm.Disk))
            {
                Arm.Disk armDataDisk = (Arm.Disk)disk.SourceDisk;

                copyblobdetail.SourceSA = armDataDisk.StorageAccountName;
                copyblobdetail.SourceContainer = armDataDisk.StorageAccountContainer;
                copyblobdetail.SourceBlob = armDataDisk.StorageAccountBlob;

                if (armDataDisk.SourceStorageAccount != null && armDataDisk.SourceStorageAccount.Keys != null)
                    copyblobdetail.SourceKey = armDataDisk.SourceStorageAccount.Keys[0].Value;
            }
            copyblobdetail.DestinationSA = disk.TargetStorageAccount.ToString();
            copyblobdetail.DestinationContainer = disk.TargetStorageAccountContainer;
            copyblobdetail.DestinationBlob = disk.TargetStorageAccountBlob;

            return copyblobdetail;
        }

        private void BuildStorageAccountObject(MigrationTarget.StorageAccount targetStorageAccount)
        {
            LogProvider.WriteLog("BuildStorageAccountObject", "Start Microsoft.Storage/storageAccounts/" + targetStorageAccount.ToString());

            StorageAccount_Properties storageaccount_properties = new StorageAccount_Properties();
            storageaccount_properties.accountType = targetStorageAccount.AccountType;

            StorageAccount storageaccount = new StorageAccount(this.ExecutionGuid);
            storageaccount.name = targetStorageAccount.ToString();
            storageaccount.location = "[resourceGroup().location]";
            storageaccount.properties = storageaccount_properties;

            this.AddResource(storageaccount);

            LogProvider.WriteLog("BuildStorageAccountObject", "End");
        }

        private string GetVMSize(string vmsize)
        {
            Dictionary<string, string> VMSizeTable = new Dictionary<string, string>();
            VMSizeTable.Add("ExtraSmall", "Standard_A0");
            VMSizeTable.Add("Small", "Standard_A1");
            VMSizeTable.Add("Medium", "Standard_A2");
            VMSizeTable.Add("Large", "Standard_A3");
            VMSizeTable.Add("ExtraLarge", "Standard_A4");
            VMSizeTable.Add("A5", "Standard_A5");
            VMSizeTable.Add("A6", "Standard_A6");
            VMSizeTable.Add("A7", "Standard_A7");
            VMSizeTable.Add("A8", "Standard_A8");
            VMSizeTable.Add("A9", "Standard_A9");
            VMSizeTable.Add("A10", "Standard_A10");
            VMSizeTable.Add("A11", "Standard_A11");

            if (VMSizeTable.ContainsKey(vmsize))
            {
                return VMSizeTable[vmsize];
            }
            else
            {
                return vmsize;
            }
        }

        public JObject GetTemplate()
        {
            if (!TemplateStreams.ContainsKey("export.json"))
                return null;

            MemoryStream templateStream = TemplateStreams["export.json"];
            templateStream.Position = 0;
            StreamReader sr = new StreamReader(templateStream);
            String myStr = sr.ReadToEnd();

            return JObject.Parse(myStr);
        }

        private async Task<string> GetTemplateString()
        {
            Template template = new Template()
            {
                resources = this.Resources,
                parameters = this.Parameters
            };

            // save JSON template
            string jsontext = JsonConvert.SerializeObject(template, Newtonsoft.Json.Formatting.Indented, new JsonSerializerSettings { NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore });
            jsontext = jsontext.Replace("schemalink", "$schema");

            return jsontext;
        }

        public string GetCopyBlobDetailPath()
        {
            return Path.Combine(this.OutputDirectory, "copyblobdetails.json");
        }


        public string GetTemplatePath()
        {
            return Path.Combine(this.OutputDirectory, "export.json");
        }

        public string GetInstructionPath()
        {
            return Path.Combine(this.OutputDirectory, "DeployInstructions.html");
        }
    }
}





//private void BuildPublicIPAddressObject(Asm.VirtualMachine asmVirtualMachine)
//{
//    LogProvider.WriteLog("BuildPublicIPAddressObject", "Start");

//    string publicipaddress_name = asmVirtualMachine.LoadBalancerName;

//    string publicipallocationmethod = "Dynamic";
//    if (asmVirtualMachine.Parent.AsmReservedIP != null)
//        publicipallocationmethod = "Static";

//    Hashtable dnssettings = new Hashtable();
//    dnssettings.Add("domainNameLabel", (publicipaddress_name + _settingsProvider.StorageAccountSuffix).ToLower());

//    PublicIPAddress_Properties publicipaddress_properties = new PublicIPAddress_Properties();
//    publicipaddress_properties.dnsSettings = dnssettings;
//    publicipaddress_properties.publicIPAllocationMethod = publicipallocationmethod;

//    PublicIPAddress publicipaddress = new PublicIPAddress(this.ExecutionGuid);
//    publicipaddress.name = publicipaddress_name + _settingsProvider.PublicIPSuffix;
//    publicipaddress.location = "[resourceGroup().location]";
//    publicipaddress.properties = publicipaddress_properties;

//    this.AddResource(publicipaddress);

//    LogProvider.WriteLog("BuildPublicIPAddressObject", "End");
//}


