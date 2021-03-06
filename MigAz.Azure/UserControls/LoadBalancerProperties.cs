﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MigAz.Azure.UserControls
{
    public partial class LoadBalancerProperties : UserControl
    {
        private Azure.MigrationTarget.LoadBalancer _LoadBalancer;

        public delegate Task AfterPropertyChanged();
        public event AfterPropertyChanged PropertyChanged;

        public LoadBalancerProperties()
        {
            InitializeComponent();
        }

        internal async Task Bind(MigrationTarget.LoadBalancer loadBalancer)
        {
            _LoadBalancer = loadBalancer;
            txtTargetName.Text = _LoadBalancer.Name;

            if (_LoadBalancer.FrontEndIpConfigurations.Count > 0 &&
                _LoadBalancer.FrontEndIpConfigurations[0].PublicIp != null)
            {
                // todo, this if statement above is temporary.  Property control needs more build out to specific public vs private load balancer, selecting public ip if public and the vnet/subnet only if internal
                // the enabled statements below are temporary until this selection is added
                rbExistingARMVNet.Enabled = false;
                rbVNetInMigration.Enabled = false;
                cmbExistingArmSubnet.Enabled = false;
                cmbExistingArmVNets.Enabled = false;
            }
            else
            {
                if (rbExistingARMVNet.Enabled == false ||
                        _LoadBalancer == null ||
                        _LoadBalancer.FrontEndIpConfigurations.Count == 0 ||
                        _LoadBalancer.FrontEndIpConfigurations[0].TargetSubnet == null ||
                        _LoadBalancer.FrontEndIpConfigurations[0].TargetSubnet.GetType() == typeof(Azure.MigrationTarget.Subnet)
                    )
                {
                    rbVNetInMigration.Checked = true;
                }
                else
                {
                    rbExistingARMVNet.Checked = true;
                }
            }
        }

        private void txtTargetName_TextChanged(object sender, EventArgs e)
        {
            TextBox txtSender = (TextBox)sender;

            _LoadBalancer.Name = txtSender.Text;

            PropertyChanged();
        }

        private async void rbVNetInMigration_CheckedChanged(object sender, EventArgs e)
        {
            RadioButton rb = (RadioButton)sender;

            if (rb.Checked)
            {
                #region Add "In MigAz Migration" Virtual Networks to cmbExistingArmVNets

                cmbExistingArmVNets.Items.Clear();
                cmbExistingArmSubnet.Items.Clear();

                // todo now asap russell
                //TreeNode targetResourceGroupNode = _AsmToArmForm.SeekARMChildTreeNode(_AsmToArmForm.TargetResourceGroup.ToString(), _AsmToArmForm.TargetResourceGroup.ToString(), _AsmToArmForm.TargetResourceGroup, false);

                //foreach (TreeNode treeNode in targetResourceGroupNode.Nodes)
                //{
                //    if (treeNode.Tag != null && treeNode.Tag.GetType() == typeof(Azure.MigrationTarget.VirtualNetwork))
                //    {
                //        Azure.MigrationTarget.VirtualNetwork targetVirtualNetwork = (Azure.MigrationTarget.VirtualNetwork)treeNode.Tag;
                //        cmbExistingArmVNets.Items.Add(targetVirtualNetwork);
                //    }
                //}

                #endregion

                #region Seek Target VNet and Subnet as ComboBox SelectedItems

                if (_LoadBalancer != null && _LoadBalancer.FrontEndIpConfigurations.Count > 0)
                {
                    if (_LoadBalancer.FrontEndIpConfigurations[0].TargetVirtualNetwork != null)
                    {
                        // Attempt to match target to list items
                        foreach (Azure.MigrationTarget.VirtualNetwork listVirtualNetwork in cmbExistingArmVNets.Items)
                        {
                            if (listVirtualNetwork.ToString() == _LoadBalancer.FrontEndIpConfigurations[0].TargetVirtualNetwork.ToString())
                            {
                                cmbExistingArmVNets.SelectedItem = listVirtualNetwork;
                                break;
                            }
                        }

                        if (cmbExistingArmVNets.SelectedItem != null && _LoadBalancer.FrontEndIpConfigurations[0].TargetSubnet != null)
                        {
                            foreach (Azure.MigrationTarget.Subnet listSubnet in cmbExistingArmSubnet.Items)
                            {
                                if (listSubnet.ToString() == _LoadBalancer.FrontEndIpConfigurations[0].TargetSubnet.ToString())
                                {
                                    cmbExistingArmSubnet.SelectedItem = listSubnet;
                                    break;
                                }
                            }
                        }
                    }
                }

                #endregion
            }

            await PropertyChanged();
        }

        private async void rbExistingARMVNet_CheckedChanged(object sender, EventArgs e)
        {
            RadioButton rb = (RadioButton)sender;

            if (rb.Checked)
            {

                #region Add "In MigAz Migration" Virtual Networks to cmbExistingArmVNets

                cmbExistingArmVNets.Items.Clear();
                cmbExistingArmSubnet.Items.Clear();

                // todo now asap russell 
                //foreach (Azure.Arm.VirtualNetwork armVirtualNetwork in await _AsmToArmForm.AzureContextTargetARM.AzureRetriever.GetAzureARMVirtualNetworks())
                //{
                //    if (armVirtualNetwork.HasNonGatewaySubnet)
                //        cmbExistingArmVNets.Items.Add(armVirtualNetwork);
                //}

                #endregion

                #region Seek Target VNet and Subnet as ComboBox SelectedItems

                if (_LoadBalancer != null && _LoadBalancer.FrontEndIpConfigurations.Count > 0)
                {
                    if (_LoadBalancer.FrontEndIpConfigurations[0].TargetVirtualNetwork != null)
                    {
                        // Attempt to match target to list items
                        for (int i = 0; i < cmbExistingArmVNets.Items.Count; i++)
                        {
                            Azure.Arm.VirtualNetwork listVirtualNetwork = (Azure.Arm.VirtualNetwork)cmbExistingArmVNets.Items[i];
                            if (listVirtualNetwork.ToString() == _LoadBalancer.FrontEndIpConfigurations[0].TargetVirtualNetwork.ToString())
                            {
                                cmbExistingArmVNets.SelectedIndex = i;
                                break;
                            }
                        }
                    }

                    if (_LoadBalancer.FrontEndIpConfigurations[0].TargetSubnet != null)
                    {
                        // Attempt to match target to list items
                        for (int i = 0; i < cmbExistingArmSubnet.Items.Count; i++)
                        {
                            Azure.Arm.Subnet listSubnet = (Azure.Arm.Subnet)cmbExistingArmSubnet.Items[i];
                            if (listSubnet.ToString() == _LoadBalancer.FrontEndIpConfigurations[0].TargetSubnet.ToString())
                            {
                                cmbExistingArmSubnet.SelectedIndex = i;
                                break;
                            }
                        }
                    }
                }
                #endregion

            }

            await PropertyChanged();
        }

        private async void cmbExistingArmVNets_SelectedIndexChanged(object sender, EventArgs e)
        {
            cmbExistingArmSubnet.Items.Clear();
            if (cmbExistingArmVNets.SelectedItem != null)
            {
                if (cmbExistingArmVNets.SelectedItem.GetType() == typeof(Azure.MigrationTarget.VirtualNetwork))
                {
                    Azure.MigrationTarget.VirtualNetwork selectedNetwork = (Azure.MigrationTarget.VirtualNetwork)cmbExistingArmVNets.SelectedItem;

                    foreach (Azure.MigrationTarget.Subnet subnet in selectedNetwork.TargetSubnets)
                    {
                        if (!subnet.IsGatewaySubnet)
                            cmbExistingArmSubnet.Items.Add(subnet);
                    }
                }
                else if (cmbExistingArmVNets.SelectedItem.GetType() == typeof(Azure.Arm.VirtualNetwork))
                {
                    Azure.Arm.VirtualNetwork selectedNetwork = (Azure.Arm.VirtualNetwork)cmbExistingArmVNets.SelectedItem;

                    foreach (Azure.Arm.Subnet subnet in selectedNetwork.Subnets)
                    {
                        if (!subnet.IsGatewaySubnet)
                            cmbExistingArmSubnet.Items.Add(subnet);
                    }
                }
            }

            await PropertyChanged();
        }

        private void cmbExistingArmSubnet_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_LoadBalancer != null && _LoadBalancer.FrontEndIpConfigurations.Count > 0)
            {
                if (cmbExistingArmSubnet.SelectedItem == null)
                {
                    _LoadBalancer.FrontEndIpConfigurations[0].TargetVirtualNetwork = null;
                    _LoadBalancer.FrontEndIpConfigurations[0].TargetSubnet = null;
                }
                else
                {
                    if (cmbExistingArmSubnet.SelectedItem.GetType() == typeof(Azure.MigrationTarget.Subnet))
                    {
                        _LoadBalancer.FrontEndIpConfigurations[0].TargetVirtualNetwork = (Azure.MigrationTarget.VirtualNetwork)cmbExistingArmVNets.SelectedItem;
                        _LoadBalancer.FrontEndIpConfigurations[0].TargetSubnet = (Azure.MigrationTarget.Subnet)cmbExistingArmSubnet.SelectedItem;
                    }
                    else if (cmbExistingArmSubnet.SelectedItem.GetType() == typeof(Azure.Arm.Subnet))
                    {
                        _LoadBalancer.FrontEndIpConfigurations[0].TargetVirtualNetwork = (Azure.Arm.VirtualNetwork)cmbExistingArmVNets.SelectedItem;
                        _LoadBalancer.FrontEndIpConfigurations[0].TargetSubnet = (Azure.Arm.Subnet)cmbExistingArmSubnet.SelectedItem;
                    }
                }
            }

            PropertyChanged();
        }
    }
}
